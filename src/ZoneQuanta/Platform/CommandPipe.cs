using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Hands command-line arguments from a second launch (Explorer verb, script, Claude Code) to the
// running instance. Only the same user can connect, in either direction.
internal sealed class CommandPipe : IDisposable
{
    private const int MaxBytes = 64 * 1024;
    private static readonly string Name = $"ZoneQuanta.Cmd.7F3A.{Process.GetCurrentProcess().SessionId}";
    private readonly CancellationTokenSource _cts = new();

    public event Action<string[]>? Received;

    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        var buffer = new byte[MaxBytes];
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token);
                int total = 0, read;
                while (total < MaxBytes && (read = await server.ReadAsync(buffer.AsMemory(total, MaxBytes - total), _cts.Token)) > 0)
                    total += read;
                var args = Encoding.UTF8.GetString(buffer, 0, total).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 0) Received?.Invoke(args);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Error("pipe", ex);
                try { await Task.Delay(300, _cts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public static bool Send(string[] args)
    {
        try
        {
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(6000);
            client.Write(Encoding.UTF8.GetBytes(string.Join('\n', args)));
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Error("pipe-send", ex);
            return false;
        }
    }

    public void Dispose() => _cts.Cancel();
}
