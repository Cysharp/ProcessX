using Xunit;
using static ProcessX.Tests.TestSetup;
using static ProcessX.Zx;

namespace ProcessX.Tests;

// What each shell is set up to do with a failure. A test returns early when its shell is not installed.
public class ShellTest
{
    static async Task WithShellAsync(Shell value, Func<Task> body)
    {
        var before = shell;
        shell = value;
        try
        {
            await body();
        }
        finally
        {
            shell = before;
        }
    }

    [Fact]
    public async Task PrefixAndPostfixWrapScript()
    {
        await WithShellAsync(new Shell("some-shell", "-c") { Prefix = "before; ", Postfix = "; after" }, () =>
        {
            var command = sh("script");
            Assert.Equal("some-shell", command.FileName);
            Assert.Equal(["-c", "before; script; after"], command.Arguments);
            return Task.CompletedTask;
        });

        await WithShellAsync(new Shell("some-shell", "-c"), () =>
        {
            Assert.Equal(["-c", "script"], sh("script").Arguments);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PwshPassesOnExitCodeOfNativeCommand()
    {
        if (which("pwsh") == null) return;

        await WithShellAsync(Shell.Pwsh, async () =>
        {
            var result = await sh($"dotnet {Helper} exit 3").NoThrow();
            Assert.Equal(3, result.ExitCode);

            // a native failure is not forgotten when the script goes on
            var later = await sh($"dotnet {Helper} exit 3; Write-Output after").NoThrow();
            Assert.Equal(3, later.ExitCode);
            Assert.Equal("after", later.StdOut);
        });
    }

    [Fact]
    public async Task PwshCmdletErrorFailsScript()
    {
        if (which("pwsh") == null) return;

        await WithShellAsync(Shell.Pwsh, async () =>
        {
            var result = await sh("Get-Item zx-this-path-does-not-exist; Write-Output after").NoThrow();
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("", result.StdOut); // stopped at the error
        });
    }

    [Fact]
    public async Task PwshSucceeds()
    {
        if (which("pwsh") == null) return;

        await WithShellAsync(Shell.Pwsh, async () =>
        {
            var result = await sh("Write-Output hello");
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("hello", result.StdOut);
        });
    }

    [Fact]
    public async Task BashFailsOnFailureInsidePipe()
    {
        // `bash` on Windows may be the WSL launcher, which is a different environment
        if (OperatingSystem.IsWindows() || which("bash") == null) return;

        await WithShellAsync(Shell.Bash, async () =>
        {
            Assert.NotEqual(0, (await sh("false | cat").NoThrow()).ExitCode);

            var stopped = await sh("false; echo after").NoThrow();
            Assert.NotEqual(0, stopped.ExitCode);
            Assert.Equal("", stopped.StdOut);

            Assert.NotEqual(0, (await sh("echo $ZX_THIS_VARIABLE_IS_NOT_SET").NoThrow()).ExitCode);

            Assert.Equal("ok", await sh("true | cat; echo ok"));
        });

        // without the prefix, bash reports only the last command of a pipe
        await WithShellAsync(new Shell("bash", "-c"), async () =>
        {
            Assert.Equal(0, (await sh("false | cat").NoThrow()).ExitCode);
        });
    }

    [Fact]
    public async Task ShStopsAtFailure()
    {
        if (OperatingSystem.IsWindows()) return;

        await WithShellAsync(Shell.Sh, async () =>
        {
            var stopped = await sh("false; echo after").NoThrow();
            Assert.NotEqual(0, stopped.ExitCode);
            Assert.Equal("", stopped.StdOut);

            Assert.Equal("ok", await sh("echo ok"));
        });
    }

    [Fact]
    public async Task DefaultShellOnUnixFailsOnFailureInsidePipe()
    {
        if (OperatingSystem.IsWindows() || which("bash") == null) return;

        Assert.NotEqual(0, (await sh("false | cat").NoThrow()).ExitCode);
    }
}
