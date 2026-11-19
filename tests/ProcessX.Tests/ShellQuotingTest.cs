using Xunit;
using static ProcessX.Tests.TestSetup;
using static ProcessX.Zx;

namespace ProcessX.Tests;

// sh($"... {value} ..."): the text is the script, each hole is a value quoted for the shell.
public class ShellQuotingTest
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

    /// <summary>The script that sh(...) would hand to a shell with the given quoting. No process is started.</summary>
    static string Script(ShellQuoting quoting, Func<Command> create)
    {
        var before = shell;
        shell = new Shell("some-shell", "-c") { Quoting = quoting };
        try
        {
            return create().Arguments[^1];
        }
        finally
        {
            shell = before;
        }
    }

    [Fact]
    public void TextIsPassedAsItIs()
    {
        Assert.Equal("cat 'a b' | grep \"x\" > out.txt", Script(ShellQuoting.Posix, () => sh("cat 'a b' | grep \"x\" > out.txt")));
        Assert.Equal("cat 'a b' | grep \"x\" > out.txt", Script(ShellQuoting.Cmd, () => sh($"cat 'a b' | grep \"x\" > out.txt")));
    }

    [Fact]
    public void PosixQuotesWithSingleQuotes()
    {
        var value = "it's a \"test\" & $HOME $(x) `y` ;|<>\\";

        Assert.Equal("echo 'it'\\''s a \"test\" & $HOME $(x) `y` ;|<>\\' | cat", Script(ShellQuoting.Posix, () => sh($"echo {value} | cat")));
        Assert.Equal("echo ''", Script(ShellQuoting.Posix, () => sh($"echo {""}")));
    }

    [Fact]
    public void PowerShellDoublesQuotes()
    {
        var value = "it's a \"test\" & $HOME $(x) `y` ;| \u2019curly\u2019";

        Assert.Equal("echo 'it''s a \"test\" & $HOME $(x) `y` ;| \u2019\u2019curly\u2019\u2019'", Script(ShellQuoting.PowerShell, () => sh($"echo {value}")));
    }

    [Fact]
    public void CmdQuotesWithDoubleQuotes()
    {
        var value = "a b & echo pwned | (x) ^ <> !";

        Assert.Equal("echo \"a b & echo pwned | (x) ^ <> !\" | sort", Script(ShellQuoting.Cmd, () => sh($"echo {value} | sort")));
        Assert.Equal("dir \"C:\\dir with space\\\\\"", Script(ShellQuoting.Cmd, () => sh($"dir {@"C:\dir with space\"}")));
        Assert.Equal("echo \"\"", Script(ShellQuoting.Cmd, () => sh($"echo {""}")));
    }

    [Theory]
    [InlineData("say \"hi\"")]
    [InlineData("%PATH%")]
    [InlineData("100%")]
    [InlineData("line1\nline2")]
    [InlineData("line1\rline2")]
    public void CmdRejectsWhatItCanNotQuote(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => Script(ShellQuoting.Cmd, () => sh($"echo {value}")));
        Assert.Contains("cmd.exe", ex.Message);
        Assert.Contains("run(", ex.Message);

        // the same value is fine as raw text, and in the other shells
        Assert.Equal("echo " + value, Script(ShellQuoting.Cmd, () => sh($"echo {value:raw}")));
        Script(ShellQuoting.Posix, () => sh($"echo {value}"));
        Script(ShellQuoting.PowerShell, () => sh($"echo {value}"));
    }

    [Fact]
    public void RawInsertsScriptText()
    {
        var filter = "| sort";
        var value = "a b";

        Assert.Equal("echo 'a b' | sort", Script(ShellQuoting.Posix, () => sh($"echo {value} {filter:raw}")));
    }

    [Fact]
    public void HoleJoinsWithAdjacentText()
    {
        var name = "A B";

        Assert.Equal("git log --author='A B'", Script(ShellQuoting.Posix, () => sh($"git log --author={name}")));
        Assert.Equal("git log --author=\"A B\"", Script(ShellQuoting.Cmd, () => sh($"git log --author={name}")));
    }

    [Fact]
    public void SequenceNullAndOtherTypes()
    {
        string[] files = ["a b.txt", "c.txt"];
        string? nothing = null;

        Assert.Equal("rm 'a b.txt' 'c.txt'", Script(ShellQuoting.Posix, () => sh($"rm {files}")));
        Assert.Equal("rm a b.txt c.txt", Script(ShellQuoting.Posix, () => sh($"rm {files:raw}")));
        Assert.Equal("rm  -f", Script(ShellQuoting.Posix, () => sh($"rm {nothing} -f")));
        Assert.Equal("rm  -f", Script(ShellQuoting.Posix, () => sh($"rm {Array.Empty<string>()} -f")));
        Assert.Equal("head -n '10' --x='FF'", Script(ShellQuoting.Posix, () => sh($"head -n {10} --x={255:X}")));
    }

    [Fact]
    public void ConstantHoleIsQuotedToo()
    {
        const string Value = "a b";

        Assert.Equal("echo 'a b'", Script(ShellQuoting.Posix, () => sh($"echo {Value}")));
        Assert.Equal("echo 'a b'", Script(ShellQuoting.Posix, () => sh($"echo {"a b"}")));
    }

    [Fact]
    public async Task ValueReachesProcessThroughDefaultShell()
    {
        // what the default shell of this OS can quote: cmd.exe on Windows, bash elsewhere
        var value = OperatingSystem.IsWindows()
            ? "a b & echo pwned | (x) ^ <> ! 'single'"
            : "it's a \"test\" & echo pwned | $HOME $(x) `y` ;<>\\ %PATH%";

        var result = await sh($"dotnet {Helper} args {value} end");
        Assert.Equal([value, "end"], result.Lines);

        var piped = await sh($"dotnet {Helper} args {value} | dotnet {Helper} upper");
        Assert.Equal(value.ToUpperInvariant(), piped.StdOut);
    }

    [Fact]
    public async Task ValueReachesProcessThroughPwsh()
    {
        if (which("pwsh") == null) return;

        await WithShellAsync(Shell.Pwsh, async () =>
        {
            var value = "it's a & echo pwned | $HOME $(x) `y` ;<> %PATH% \u2019curly\u2019";

            var result = await sh($"dotnet {Helper} args {value} end").WithEncoding(System.Text.Encoding.UTF8);
            Assert.Equal([value, "end"], result.Lines);
        });
    }

    [Fact]
    public async Task ValueReachesProcessThroughBashAndSh()
    {
        if (OperatingSystem.IsWindows()) return;

        var value = "it's a \"test\" & echo pwned | $HOME $(x) `y` ;<>\\ %PATH%";

        foreach (var target in new[] { Shell.Bash, Shell.Sh })
        {
            if (target == Shell.Bash && which("bash") == null) continue;

            await WithShellAsync(target, async () =>
            {
                var result = await sh($"dotnet {Helper} args {value} end");
                Assert.Equal([value, "end"], result.Lines);
            });
        }
    }
}
