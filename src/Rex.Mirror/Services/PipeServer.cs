using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;
using Rex.Core;

namespace Rex.Mirror.Services;

/// <summary>Accepts one JSON request per connection on the per-user pipe and answers on the UI thread.</summary>
public sealed class PipeServer : IDisposable
{
    private readonly AppHost _host;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public PipeServer(AppHost host) => _host = host;

    public void Start() => _ = Task.Run(() => ListenAsync(_cts.Token));

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(Ipc.PipeName(), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                // ServeAsync owns this instance from here. Do not capture the loop variable:
                // it is cleared below before a queued delegate may start running.
                _ = ServeAsync(pipe, cancellationToken);
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

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                var request = line is null ? null : Ipc.ParseRequest(line);

                IpcResponse response;
                if (request is null)
                {
                    response = IpcResponse.Fail("Malformed request.");
                }
                else
                {
                    response = await _dispatcher.InvokeAsync(() => CommandRouter.HandleAsync(_host, request)).Task.Unwrap().ConfigureAwait(false);
                }

                var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(Ipc.Serialize(response)).ConfigureAwait(false);
                pipe.WaitForPipeDrain();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Client went away; nothing to answer.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
