using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ProcessX;

/// <summary>
/// Turns termination signals into a CancellationToken, so that a script can clean up instead of being killed.
/// Same approach as PosixSignalHandler of ConsoleAppFramework: SIGINT (Ctrl+C), SIGQUIT (Ctrl+Break) and SIGTERM
/// are taken over, the token is canceled, and the script gets a limited time to finish before the process exits with 130.
/// </summary>
internal sealed class TerminateSignal
{
    // 128 + SIGINT, the conventional exit code of a process ended by Ctrl+C
    const int CanceledExitCode = 130;

    // Stopwatch timestamp of the first signal that the system also delivered to the child processes; 0 = none yet
    static long deliveredToChildrenAt;

    readonly CancellationTokenSource source = new();

    // held for the lifetime of the process; a registration stops working when it is collected
    readonly PosixSignalRegistration[] registrations;

    int signaled;

    public TerminateSignal()
    {
        Action<PosixSignalContext> handler = HandleSignal;
        registrations =
        [
            PosixSignalRegistration.Create(PosixSignal.SIGINT, handler),
            PosixSignalRegistration.Create(PosixSignal.SIGQUIT, handler),
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, handler),
        ];
    }

    public CancellationToken Token => source.Token;

    /// <summary>
    /// True when a process started at <paramref name="startTimestamp"/> has received the signal by itself:
    /// the signal came from the console and the process was already running.
    /// </summary>
    public static bool WasDeliveredToProcessesStartedAt(long startTimestamp)
    {
        var deliveredAt = Volatile.Read(ref deliveredToChildrenAt);
        return deliveredAt != 0 && startTimestamp <= deliveredAt;
    }

    void HandleSignal(PosixSignalContext context)
    {
        // do not terminate now; the script observes the token and ends by itself
        context.Cancel = true;

        // Ctrl+C and Ctrl+Break reach every process attached to the console (Windows) or in the foreground
        // process group (Unix), so the children have it too. SIGTERM on Unix is sent to one process only.
        if (OperatingSystem.IsWindows() || context.Signal is PosixSignal.SIGINT or PosixSignal.SIGQUIT)
        {
            Interlocked.CompareExchange(ref deliveredToChildrenAt, Stopwatch.GetTimestamp(), 0);
        }

        if (Interlocked.Exchange(ref signaled, 1) != 0) return;

        Environment.ExitCode = CanceledExitCode;

        // armed before Cancel, because Cancel can run the continuations of the script inline
        _ = ExitAfterTimeoutAsync(Zx.terminateTimeout);

        source.Cancel();
    }

    // a script that ignores the token, or whose cleanup hangs, must still end
    static async Task ExitAfterTimeoutAsync(TimeSpan timeout)
    {
        await Task.Delay(timeout).ConfigureAwait(false);
        Environment.Exit(CanceledExitCode);
    }
}
