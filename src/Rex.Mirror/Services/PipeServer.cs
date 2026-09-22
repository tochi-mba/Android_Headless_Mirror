using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>Accepts one bounded JSON request per connection on the per-user pipe and answers on the UI thread.</summary>
public sealed class PipeServer : IDisposable
{
    internal const int MaxRequestBytes = 64 * 1024;
    internal static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);
    private const int MaxConcurrentClients = 16;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly AppHost _host;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly SemaphoreSlim _clientSlots = new(MaxConcurrentClients, MaxConcurrentClients);

    public PipeServer(AppHost host) => _host = host;

    public void Start() => _ = Task.Run(() => ListenAsync(_cts.Token));

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    Ipc.PipeName(),
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                // ServeClientAsync owns this instance from here. Do not capture the loop variable:
                // it is cleared below before a queued continuation may start running.
                _ = ServeClientAsync(pipe, cancellationToken);
                pipe = null;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _host.Log.Warn("Pipe server error: " + ex.Message);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Anything else means the CLI would silently stop working; make it visible and give up.
                _host.Log.Error("Pipe server stopped", ex);
                return;
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var slotAcquired = false;
        var answered = false;
        try
        {
            await _clientSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            slotAcquired = true;

            await using (pipe)
            {
                string? line;
                using (var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    readDeadline.CancelAfter(RequestReadTimeout);
                    try
                    {
                        line = await ReadBoundedLineAsync(pipe, readDeadline.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        await WriteResponseAsync(pipe, IpcResponse.Fail("Request timed out."), cancellationToken).ConfigureAwait(false);
                        answered = true;
                        return;
                    }
                    catch (InvalidDataException ex)
                    {
                        await WriteResponseAsync(pipe, IpcResponse.Fail(ex.Message), cancellationToken).ConfigureAwait(false);
                        answered = true;
                        return;
                    }
                    catch (DecoderFallbackException)
                    {
                        await WriteResponseAsync(pipe, IpcResponse.Fail("Request is not valid UTF-8."), cancellationToken).ConfigureAwait(false);
                        answered = true;
                        return;
                    }
                }

                IpcResponse response;
                if (line is null)
                {
                    response = IpcResponse.Fail("Malformed request.");
                }
                else if (!Ipc.TryParseRequest(line, out var request, out var parseError))
                {
                    response = IpcResponse.Fail(parseError);
                }
                else
                {
                    response = await _dispatcher
                        .InvokeAsync(() => CommandRouter.HandleAsync(_host, request!))
                        .Task.Unwrap()
                        .ConfigureAwait(false);
                }

                await WriteResponseAsync(pipe, response, cancellationToken).ConfigureAwait(false);
                answered = true;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Client went away or the app is shutting down; nothing to answer.
        }
        catch (Exception ex)
        {
            // ServeClientAsync is intentionally fire-and-forget, so it must observe every
            // unexpected exception itself instead of leaving a faulted Task unobserved.
            _host.Log.Error("IPC client handler failed", ex);
            if (!answered && pipe.IsConnected)
            {
                try
                {
                    await WriteResponseAsync(pipe, IpcResponse.Fail("Internal IPC error."), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception writeEx) when (writeEx is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    // Best-effort error response only.
                }
            }
        }
        finally
        {
            if (slotAcquired)
            {
                _clientSlots.Release();
            }
            else
            {
                pipe.Dispose();
            }
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var chunk = new byte[4096];

        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return bytes.Length == 0 ? null : DecodeLine(bytes);
            }

            for (var i = 0; i < count; i++)
            {
                if (chunk[i] == (byte)'\n')
                {
                    return DecodeLine(bytes);
                }

                if (bytes.Length >= MaxRequestBytes)
                {
                    throw new InvalidDataException($"Request exceeds the {MaxRequestBytes}-byte limit.");
                }

                bytes.WriteByte(chunk[i]);
            }
        }
    }

    private static string DecodeLine(MemoryStream bytes)
    {
        var buffer = bytes.ToArray();
        var length = buffer.Length;
        if (length > 0 && buffer[length - 1] == (byte)'\r')
        {
            length--;
        }

        return StrictUtf8.GetString(buffer, 0, length);
    }

    private static async Task WriteResponseAsync(
        NamedPipeServerStream pipe,
        IpcResponse response,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));

        var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(Ipc.Serialize(response).AsMemory(), deadline.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
