using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using static ProcessX.Tests.TestSetup;
using static ProcessX.Zx;

namespace ProcessX.Tests;

// terminateToken and stopping processes, tested from outside: the helper is the script, and this test sends it a signal.
public class TerminateTest
{
    const uint CtrlBreakEvent = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    // A console signal (Ctrl+C) reaches the script and its children together. That can be reproduced on Windows
    // (a signal to the process group) and on Linux (setsid + kill of the process group).
    static bool CanSendConsoleSignal => OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && which("setsid") != null);

    static string NewMarkerPath() => Path.Combine(Path.GetTempPath(), "zx-test-" + Guid.NewGuid().ToString("N") + ".marker");

    static int StoppedInMilliseconds(List<string> lines) => int.Parse(lines.Single(x => x.StartsWith("stopped-in-ms=")).Split('=')[1]);

    [Fact]
    public async Task SignalCancelsTokenAndScriptCleansUp()
    {
        var (lines, exitCode) = await RunAndSignalAsync(consoleSignal: false, "terminate", "30");

        Assert.Equal(["ready", "canceled", "cleanup"], lines);
        Assert.Equal(130, exitCode);
    }

    [Fact]
    public async Task ScriptThatIgnoresTokenExitsAfterTimeout()
    {
        var sw = Stopwatch.StartNew();
        var (lines, exitCode) = await RunAndSignalAsync(consoleSignal: false, "terminate-hang", "1");

        Assert.Equal(["ready"], lines);
        Assert.Equal(130, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), "was not forced to exit: " + sw.Elapsed);
    }

    [Fact]
    public async Task ChildGetsTimeToCleanUpAfterConsoleSignal()
    {
        if (!CanSendConsoleSignal) return;

        var marker = NewMarkerPath();
        try
        {
            // the child needs 1 second to clean up; the default grace period is longer than that
            var (lines, _) = await RunAndSignalAsync(consoleSignal: true, "terminate-child", "graceful", "-1", marker, "1000");

            Assert.Equal(["ready", "canceled"], lines.Take(2));
            Assert.True(File.Exists(marker), "the child was killed before it finished cleaning up");
            Assert.InRange(StoppedInMilliseconds(lines), 900, 20000);
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task GracePeriodZeroKillsImmediately()
    {
        if (!CanSendConsoleSignal) return;

        var marker = NewMarkerPath();
        try
        {
            var (lines, _) = await RunAndSignalAsync(consoleSignal: true, "terminate-child", "graceful", "0", marker, "3000");

            Assert.Equal(["ready", "canceled"], lines.Take(2));
            Assert.False(File.Exists(marker), "the child was not killed");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task ChildThatIgnoresSignalIsKilledAfterGracePeriod()
    {
        if (!CanSendConsoleSignal) return;

        var (lines, _) = await RunAndSignalAsync(consoleSignal: true, "terminate-child", "stubborn", "1");

        Assert.StartsWith("ready ", lines[0]);
        Assert.Equal("canceled", lines[1]);
        Assert.InRange(StoppedInMilliseconds(lines), 900, 20000); // waited for the grace period, then killed

        var childProcessId = int.Parse(lines[0].Split(' ')[1]);
        Assert.True(await WaitForProcessExitAsync(childProcessId, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TimeoutAsksProcessToStopWhereThatIsPossible()
    {
        var marker = NewMarkerPath();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(async () => await run($"dotnet {Helper} graceful {marker} 200").Timeout(2));

            // Linux/macOS: SIGTERM is sent and the process cleans up. Windows can not signal a single process, so it is killed.
            Assert.Equal(!OperatingSystem.IsWindows(), File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task TimeoutKillsShellCommandImmediately()
    {
        var marker = NewMarkerPath();
        try
        {
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(async () => await sh($"dotnet {Helper} graceful {marker} 200").Timeout(2));

            // a signaled shell would leave its children behind, so the whole tree is killed without asking
            Assert.False(File.Exists(marker));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4.5), "waited for a grace period: " + sw.Elapsed);
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task ProcessIsGoneWhenAwaitReturns()
    {
        var processId = 0;
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var line in run($"dotnet {Helper} pid-and-sleep 60000").Timeout(2).GracePeriod(0))
            {
                processId = int.Parse(line);
            }
        });

        // no polling: stopping waits until the process has really exited
        Assert.NotEqual(0, processId);
        Assert.True(await WaitForProcessExitAsync(processId, TimeSpan.FromMilliseconds(1)));
    }

    static async Task<(List<string> Lines, int ExitCode)> RunAndSignalAsync(bool consoleSignal, params string[] helperArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        if (OperatingSystem.IsWindows())
        {
            // on Windows a console signal can only be sent to a process group
            startInfo.FileName = "dotnet";
            startInfo.CreateNewProcessGroup = true;
        }
        else if (consoleSignal)
        {
            // own session = own process group, whose id is the process id
            startInfo.FileName = "setsid";
            startInfo.ArgumentList.Add("dotnet");
        }
        else
        {
            startInfo.FileName = "dotnet";
        }

        startInfo.ArgumentList.Add(Helper);
        foreach (var argument in helperArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        try
        {
            var lines = new List<string>();

            // "ready": the signal handlers are registered
            lines.Add((await process.StandardOutput.ReadLineAsync())!);

            if (OperatingSystem.IsWindows())
            {
                // SafeProcessHandle.Signal does not support these signals on Windows, and Ctrl+C cannot be sent
                // to a process group, so this sends Ctrl+Break (SIGQUIT), which goes through the same handler.
                // It reaches the whole group: the script and the processes it started.
                Assert.True(GenerateConsoleCtrlEvent(CtrlBreakEvent, (uint)process.Id), "could not send Ctrl+Break");
            }
            else if (consoleSignal)
            {
                // like Ctrl+C in a terminal: SIGINT to every process of the group (a negative id is a process group;
                // no `--` in front of it, dash does not accept that)
                using var kill = Process.Start("/bin/sh", ["-c", $"kill -INT -{process.Id}"]);
                await kill.WaitForExitAsync();
                Assert.Equal(0, kill.ExitCode);
            }
            else
            {
                Assert.True(process.SafeHandle.Signal(PosixSignal.SIGINT), "could not send SIGINT");
            }

            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                lines.Add(line);
            }

            await process.WaitForExitAsync();
            return (lines, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
