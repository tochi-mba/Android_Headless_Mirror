using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rex.Core;

/// <summary>
/// One request, one JSON line each way, over a per-user named pipe. The desktop app hosts the
/// server; the CLI and agents are clients. Commands that need the live mirror (scrcpy shortcuts,
/// zoom, focusing the window) go through here so nothing ever synthesizes input blindly.
/// </summary>
public static class Ipc
{
    public const int ProtocolVersion = 2;

    public static readonly string[] Commands =
    [
        "ping", "status", "show", "hide", "quit",
        "action", "zoom", "screenshot", "session-restart", "session-stop", "refresh",
    ];

    public const string PipeNameOverride = "REX_PIPE_NAME";

    public static string PipeName()
    {
        var overridden = Environment.GetEnvironmentVariable(PipeNameOverride);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        string user;
        try
        {
            user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or InvalidOperationException)
        {
            user = Environment.UserName;
        }

        return "rex-mirror-" + user.Replace('\\', '_').Replace('-', '_');
    }

    public static string Serialize(IpcRequest request) =>
        new JsonObject
        {
            ["v"] = ProtocolVersion,
            ["command"] = request.Command,
            ["args"] = new JsonObject(request.Args.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
        }.ToJsonString();

    public static string Serialize(IpcResponse response) =>
        new JsonObject
        {
            ["v"] = ProtocolVersion,
            ["ok"] = response.Ok,
            ["data"] = response.Data?.DeepClone(),
            ["error"] = response.Error,
        }.ToJsonString();

    public static IpcRequest? ParseRequest(string line)
    {
        try
        {
            var node = JsonNode.Parse(line)?.AsObject();
            var command = node?["command"]?.GetValue<string>();
            if (command is null)
            {
                return null;
            }

            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            if (node!["args"] is JsonObject argsNode)
            {
                foreach (var pair in argsNode)
                {
                    args[pair.Key] = pair.Value?.ToString() ?? string.Empty;
                }
            }

            return new IpcRequest(command, args);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static IpcResponse ParseResponse(string line)
    {
        try
        {
            var node = JsonNode.Parse(line)?.AsObject();
            if (node is null)
            {
                return IpcResponse.Fail("The app returned an empty response.");
            }

            var ok = node["ok"]?.GetValue<bool>() ?? false;
            return new IpcResponse(ok, node["data"], node["error"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return IpcResponse.Fail("The app returned invalid JSON: " + ex.Message);
        }
    }
}

public sealed record IpcRequest(string Command, IReadOnlyDictionary<string, string> Args)
{
    public IpcRequest(string command) : this(command, new Dictionary<string, string>()) { }

    public string Arg(string name) => Args.TryGetValue(name, out var value) ? value : string.Empty;
}

public sealed record IpcResponse(bool Ok, JsonNode? Data, string? Error)
{
    public static IpcResponse Success(JsonNode? data = null) => new(true, data, null);
    public static IpcResponse Fail(string error) => new(false, null, error);
}

public sealed class IpcClient
{
    private readonly string _pipeName;

    public IpcClient(string? pipeName = null) => _pipeName = pipeName ?? Ipc.PipeName();

    /// <summary>
    /// Returns null when the app is not running. <paramref name="timeout"/> bounds the connection;
    /// the answer itself must arrive within <see cref="ResponseTimeout"/> so a wedged app can never
    /// hang a caller.
    /// </summary>
    public async Task<IpcResponse?> SendAsync(IpcRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)(timeout ?? TimeSpan.FromSeconds(2)).TotalMilliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ResponseTimeout);
        try
        {
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync(Ipc.Serialize(request).AsMemory(), deadline.Token).ConfigureAwait(false);

            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            return line is null ? IpcResponse.Fail("The app closed the connection without answering.") : Ipc.ParseResponse(line);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return IpcResponse.Fail("The app did not answer in time.");
        }
        catch (IOException ex)
        {
            return IpcResponse.Fail("The connection to the app failed: " + ex.Message);
        }
    }

    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    public async Task<bool> IsAppRunningAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(new IpcRequest("ping"), TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        return response is { Ok: true };
    }
}
