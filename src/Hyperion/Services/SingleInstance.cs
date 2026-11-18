using System.IO.Pipes;
using System.Text;

namespace Hyperion.Services;

/// <summary>
/// Keeps one Hyperion process per user session. Windows starts a new process for every magnet/.torrent activation,
/// so a second process forwards its argument to the first over a current-user-only named pipe and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly CancellationTokenSource _cts = new();

    public SingleInstance(string id)
    {
        _mutex = new Mutex(true, @"Local\" + id, out var first);
        IsFirst = first;
        PipeName = id + ".pipe";
    }

    public bool IsFirst { get; }
    public string PipeName { get; }

    /// <summary>First instance only: reports each line a later instance sends (empty line = "just bring the window forward"). Callbacks arrive on a pool thread.</summary>
    public void Listen(Action<string> onMessage) => _ = ListenAsync(onMessage, _cts.Token);

    async Task ListenAsync(Action<string> onMessage, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8);
                if (await reader.ReadLineAsync(ct) is { } line) onMessage(line);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // the sender disconnected mid-message; wait for the next one
            }
        }
    }

    /// <summary>Sends a message to the first instance; false if it did not answer.</summary>
    public async Task<bool> SendToFirstAsync(string message)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(3000);
            await using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(message);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* released from a different thread at shutdown; the OS frees it on exit */ }
        }
        _mutex.Dispose();
        _cts.Dispose();
    }
}
