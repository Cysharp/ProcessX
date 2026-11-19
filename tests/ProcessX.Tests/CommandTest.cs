using System.ComponentModel;
using System.Diagnostics;
using Xunit;
using ProcessX;
using static ProcessX.Tests.TestSetup;
using static ProcessX.Zx;

namespace ProcessX.Tests;

public class CommandTest
{
    static readonly string NL = Environment.NewLine;

    [Fact]
    public async Task CapturesStdOut()
    {
        var result = await run($"dotnet {Helper} args hello world");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["hello", "world"], result.Lines);
        Assert.Equal("hello" + NL + "world", result.StdOut);
        Assert.Equal("", result.StdErr);

        string text = result;
        Assert.Equal(result.StdOut, text);
        Assert.Equal(result.StdOut, $"{result}");
    }

    [Theory]
    [InlineData("a \"b\" c & echo pwned %PATH% $HOME")]
    [InlineData("with space")]
    [InlineData("'single' `backtick` $(sub) ;|<>")]
    [InlineData(@"C:\dir with space\")]
    [InlineData(@"trailing\\")]
    [InlineData(@"quote\""inside")]
    [InlineData("日本語 テキスト")]
    public async Task ArgumentReachesProcessVerbatim(string argument)
    {
        var result = await run($"dotnet {Helper} args {argument} end").WithEncoding(System.Text.Encoding.UTF8);

        Assert.Equal([argument, "end"], result.Lines);
    }

    [Fact]
    public async Task SequenceReachesProcessAsSeparateArguments()
    {
        string[] items = ["a b", "c"];
        var result = await run($"dotnet {Helper} args {items}");

        Assert.Equal(["a b", "c"], result.Lines);
    }

    [Fact]
    public async Task NonZeroExitCodeThrows()
    {
        var ex = await Assert.ThrowsAsync<ProcessErrorException>(async () => await run($"dotnet {Helper} exit 3 boom"));

        Assert.Equal(3, ex.ExitCode);
        Assert.Equal(["boom"], ex.ErrorOutput);
        Assert.Contains("ExitCode:3", ex.Message);
        Assert.Contains("boom", ex.Message);
        Assert.Contains("exit 3 boom", ex.CommandLine);
    }

    [Fact]
    public async Task NoThrowReturnsExitCode()
    {
        var result = await run($"dotnet {Helper} exit 3 boom").NoThrow();

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("boom", result.StdErr);
    }

    [Fact]
    public async Task StdErrAloneIsNotAnError()
    {
        var (stdout, stderr) = await run($"dotnet {Helper} both out err");

        Assert.Equal("out", stdout);
        Assert.Equal("err", stderr);
    }

    [Fact]
    public async Task CommandIsColdAndCanRunAgain()
    {
        var command = run($"dotnet {Helper} args x");

        Assert.Equal("x", await command);
        Assert.Equal("x", await command);
    }

    [Fact]
    public async Task UnknownCommandThrowsWithHint()
    {
        var ex = await Assert.ThrowsAsync<Win32Exception>(async () => await run($"zx-this-command-does-not-exist"));

        Assert.Contains("Command not found: 'zx-this-command-does-not-exist'", ex.Message);
        Assert.Contains("sh(", ex.Message);
    }

    [Fact]
    public async Task ShellBuiltInThrowsWithHintOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return; // echo is an executable elsewhere

        var ex = await Assert.ThrowsAsync<Win32Exception>(async () => await run($"echo 1"));
        Assert.Contains("'echo' is a built-in command of cmd.exe", ex.Message);
        Assert.Contains("sh(", ex.Message);

        // the same in a group of commands
        await Assert.ThrowsAsync<Win32Exception>(async () => await (run($"echo 1"), run($"echo 2")));
    }

    [Fact]
    public async Task BatchFileThrowsWithHintOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        // npm, code, az and many others are npm.cmd, code.cmd, az.cmd on Windows
        var directory = Directory.CreateTempSubdirectory("zx-test-").FullName;
        var path = Environment.GetEnvironmentVariable("PATH");
        try
        {
            File.WriteAllText(Path.Combine(directory, "zx-test-batch.cmd"), "@echo from batch\r\n");
            Environment.SetEnvironmentVariable("PATH", directory + ";" + path);

            var ex = await Assert.ThrowsAsync<Win32Exception>(async () => await run($"zx-test-batch"));
            Assert.Contains("'zx-test-batch' is a batch file", ex.Message);
            Assert.Contains("zx-test-batch.cmd", ex.Message, StringComparison.OrdinalIgnoreCase); // PATHEXT spells it .CMD

            // through the shell it runs
            Assert.Equal("from batch", await sh("zx-test-batch"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingShellThrowsWithHint()
    {
        var before = shell;
        shell = new Shell("zx-this-shell-does-not-exist", "-c");
        try
        {
            var ex = await Assert.ThrowsAsync<Win32Exception>(async () => await sh("echo 1"));
            Assert.Contains("Shell not found: 'zx-this-shell-does-not-exist'", ex.Message);
        }
        finally
        {
            shell = before;
        }
    }

    [Fact]
    public async Task MissingWorkingDirectoryKeepsOriginalError()
    {
        var missing = Path.Combine(Path.GetTempPath(), "zx-test-missing-" + Guid.NewGuid().ToString("N"));

        var ex = await Assert.ThrowsAsync<Win32Exception>(async () => await run($"dotnet --version").Cwd(missing));

        Assert.DoesNotContain("Command not found", ex.Message);
        Assert.DoesNotContain("sh(", ex.Message);
    }

    [Fact]
    public async Task TimeoutThrowsAndKills()
    {
        var sw = Stopwatch.StartNew();
        var processId = 0;

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var line in run($"dotnet {Helper} pid-and-sleep 60000").Timeout(TimeSpan.FromSeconds(2)))
            {
                processId = int.Parse(line);
            }
        });

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, processId);
        Assert.True(await WaitForProcessExitAsync(processId, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TimeoutKillsProcessBehindShell()
    {
        // shell -> dotnet: killing only the shell would leave dotnet running
        var processId = 0;

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var line in sh($"dotnet {Helper} pid-and-sleep 60000").Timeout(2))
            {
                processId = int.Parse(line);
            }
        });

        Assert.NotEqual(0, processId);
        Assert.True(await WaitForProcessExitAsync(processId, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task CancellationThrowsAndKills()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run($"dotnet {Helper} sleep 60000").WithCancellation(cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run($"dotnet {Helper} sleep 60000").ExecuteAsync(cts2.Token));
    }

    [Fact]
    public async Task StreamsLines()
    {
        var lines = new List<string>();
        await foreach (var line in run($"dotnet {Helper} ticks 3 10"))
        {
            lines.Add(line);
        }

        Assert.Equal(["0", "1", "2"], lines);
    }

    [Fact]
    public async Task StreamingThrowsOnNonZeroExitCode()
    {
        var ex = await Assert.ThrowsAsync<ProcessErrorException>(async () =>
        {
            await foreach (var _ in run($"dotnet {Helper} exit 4 bad")) { }
        });

        Assert.Equal(4, ex.ExitCode);
        Assert.Equal(["bad"], ex.ErrorOutput);
    }

    [Fact]
    public async Task BreakingOutOfStreamKillsProcess()
    {
        var processId = 0;
        await foreach (var line in run($"dotnet {Helper} pid-and-sleep 60000"))
        {
            processId = int.Parse(line);
            break;
        }

        Assert.True(await WaitForProcessExitAsync(processId, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task PipeConnectsProcessesWithoutShell()
    {
        var result = await (run($"dotnet {Helper} args a b c") | run($"dotnet {Helper} upper"));
        Assert.Equal(["A", "B", "C"], result.Lines);

        var count = await (run($"dotnet {Helper} args a b c") | run($"dotnet {Helper} upper") | run($"dotnet {Helper} count"));
        Assert.Equal("3", count.StdOut);

        var viaMethod = await run($"dotnet {Helper} args a").Pipe(run($"dotnet {Helper} upper"));
        Assert.Equal("A", viaMethod.StdOut);
    }

    [Fact]
    public async Task PipeFailsWhenAnyCommandFails()
    {
        var pipeline = run($"dotnet {Helper} exit 3 boom") | run($"dotnet {Helper} count");

        var ex = await Assert.ThrowsAsync<ProcessErrorException>(async () => await pipeline);
        Assert.Equal(3, ex.ExitCode);
        Assert.Equal(["boom"], ex.ErrorOutput);

        var result = await pipeline.NoThrow();
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("0", result.StdOut);
        Assert.Equal("boom", result.StdErr);
    }

    [Fact]
    public async Task ShellRunsScript()
    {
        var result = await sh($"dotnet {Helper} args a b | dotnet {Helper} upper");

        Assert.Equal(["A", "B"], result.Lines);
    }

    [Fact]
    public async Task AwaitStringRunsThroughShell()
    {
        var version = await "dotnet --version";
        Assert.Matches(@"^\d+\.\d+\.\d+", version);

        var versions = await new[] { "dotnet --version", "dotnet --version" };
        Assert.Equal([version, version], versions);
    }

    [Fact]
    public async Task AwaitStringThrowsOnNonZeroExitCode()
    {
        await Assert.ThrowsAsync<ProcessErrorException>(async () => await "exit 7");
    }

    [Fact]
    public async Task CommandsRunInParallel()
    {
        var (a, b) = await (run($"dotnet {Helper} args a"), run($"dotnet {Helper} args b"));
        Assert.Equal(("a", "b"), (a.StdOut, b.StdOut));

        var (x, y, z) = await (run($"dotnet {Helper} args x"), run($"dotnet {Helper} args y"), run($"dotnet {Helper} args z"));
        Assert.Equal(("x", "y", "z"), (x.StdOut, y.StdOut, z.StdOut));

        var results = await new[] { "1", "2", "3" }.Select(n => run($"dotnet {Helper} args {n}"));
        Assert.Equal(["1", "2", "3"], results.Select(r => r.StdOut));

        var sw = Stopwatch.StartNew();
        await (run($"dotnet {Helper} sleep 2000"), run($"dotnet {Helper} sleep 2000"), run($"dotnet {Helper} sleep 2000"), run($"dotnet {Helper} sleep 2000"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(7), "ran sequentially: " + sw.Elapsed);
    }

    [Fact]
    public async Task WorkingDirectoryAndEnvironment()
    {
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));

        Assert.Equal(temp, Path.TrimEndingDirectorySeparator(await run($"dotnet {Helper} cwd").Cwd(temp)));
        Assert.Equal("value 1", await run($"dotnet {Helper} env ZX_TEST_VAR").SetEnv("ZX_TEST_VAR", "value 1"));

        // a variable set on the script itself is inherited, and SetEnv overrides or removes it per command
        Environment.SetEnvironmentVariable("ZX_TEST_VAR", "global");
        try
        {
            Assert.Equal("global", await run($"dotnet {Helper} env ZX_TEST_VAR"));
            Assert.Equal("local", await run($"dotnet {Helper} env ZX_TEST_VAR").SetEnv("ZX_TEST_VAR", "local"));
            Assert.Equal("(null)", await run($"dotnet {Helper} env ZX_TEST_VAR").SetEnv("ZX_TEST_VAR", null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZX_TEST_VAR", null);
        }
    }

    [Fact]
    public async Task ChangeDirectory()
    {
        var before = Environment.CurrentDirectory;
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        try
        {
            cd(temp);
            Assert.Equal(temp, Path.TrimEndingDirectorySeparator(await run($"dotnet {Helper} cwd")));
        }
        finally
        {
            cd(before);
        }
    }

    [Fact]
    public async Task InheritDoesNotCapture()
    {
        var result = await run($"dotnet {Helper} args shown-by-inherit").Inherit();
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Lines);

        var failed = await run($"dotnet {Helper} exit 5").Inherit().NoThrow();
        Assert.Equal(5, failed.ExitCode);
    }

    [Fact]
    public async Task OutputIsEchoedUnlessQuiet()
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        var capturedOut = new StringWriter();
        var capturedErr = new StringWriter();
        Console.SetOut(capturedOut);
        Console.SetError(capturedErr);
        try
        {
            quiet = false; // the default; the tests turn it on globally
            await run($"dotnet {Helper} both echoed-out echoed-err");
            await run($"dotnet {Helper} both quiet-out quiet-err").Quiet();

            quiet = true;
            await run($"dotnet {Helper} both global-quiet-out global-quiet-err");
            await run($"dotnet {Helper} both loud-out loud-err").Quiet(false); // one command can opt out
        }
        finally
        {
            quiet = true;
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        Assert.Equal("echoed-out" + NL + "loud-out" + NL, capturedOut.ToString());
        Assert.Equal("echoed-err" + NL + "loud-err" + NL, capturedErr.ToString());
    }

    [Fact]
    public void WhichFindsCommand()
    {
        Assert.NotNull(which("dotnet"));
        Assert.Null(which("zx-this-command-does-not-exist"));
    }
}
