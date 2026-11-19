using System.Runtime.CompilerServices;
using Xunit;

// Zx is process-wide state (quiet, shell, current directory, console), so tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ProcessX.Tests;

static class TestSetup
{
    /// <summary>Path of the child process dll; run it as <c>run($"dotnet {Helper} mode args...")</c>.</summary>
    public static readonly string Helper = Path.Combine(AppContext.BaseDirectory, "ProcessX.Tests.Helper.dll");

    [ModuleInitializer]
    public static void Initialize()
    {
        ProcessX.Zx.quiet = true;
    }

    public static async Task<bool> WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(processId);
                if (process.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true; // not running
            }
            await Task.Delay(100);
        }
        return false;
    }
}
