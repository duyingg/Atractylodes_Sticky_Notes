using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Memo.Desktop.Windows;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopped = new();

    public SingleInstance(string profileSuffix)
    {
        using var identity = WindowsIdentity.GetCurrent();
        pipeName = $"Memo.Desktop.{identity.User!.Value}{profileSuffix}";
        mutex = new Mutex(initiallyOwned: true, $@"Global\{pipeName}", out var first);
        IsPrimary = first;
    }

    public bool IsPrimary { get; }

    public async Task SignalAsync()
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            await pipe.ConnectAsync(timeout.Token);
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var process))
                AllowSetForegroundWindow(process);
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            // The first instance may already be closing.
        }
    }

    public async Task ListenAsync(Action openSettings)
    {
        var token = stopped.Token;
        try
        {
            while (true)
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.WaitForConnectionAsync(token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var data = new byte[1];
                    if (await pipe.ReadAsync(data, timeout.Token) == 1 && data[0] == 1)
                        openSettings();
                }
                catch (IOException) { }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        stopped.Cancel();
        if (IsPrimary)
            mutex.ReleaseMutex();
        mutex.Dispose();
        stopped.Dispose();
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint process);
}
