using System.Globalization;
using Xunit;
using ProcessX;
using static ProcessX.Zx;

namespace ProcessX.Tests;

// How run($"...") becomes a file name and an argument list. No process is started here.
public class TokenizeTest
{
    static string[] Tokens(Command command) => [command.FileName, .. command.Arguments];

    [Fact]
    public void LiteralIsSplitOnWhitespace()
    {
        Assert.Equal(["git", "commit", "-m", "message"], Tokens(run($"git  commit\t-m message ")));
    }

    [Fact]
    public void PlainStringIsSplitTheSameWay()
    {
        Assert.Equal(["dotnet", "--version"], Tokens(run("dotnet --version")));

        var command = "dotnet --list-sdks";
        Assert.Equal(["dotnet", "--list-sdks"], Tokens(run(command)));
    }

    [Fact]
    public void HoleIsOneArgumentWhateverItContains()
    {
        var message = "hello \"world\" & more $HOME %PATH%";
        Assert.Equal(["git", "commit", "-m", message], Tokens(run($"git commit -m {message}")));
    }

    [Fact]
    public void HoleJoinsWithAdjacentLiteral()
    {
        var author = "A B";
        Assert.Equal(["git", "--author=A B", "x-A B-y"], Tokens(run($"git --author={author} x-{author}-y")));
    }

    [Fact]
    public void ConstantHoleIsNotSplit()
    {
        // an interpolated string whose holes are all constants is itself a constant;
        // it must still go through the handler and keep the hole as one argument
        const string Message = "a b";
        Assert.Equal(["git", "-m", "a b"], Tokens(run($"git -m {Message}")));
        Assert.Equal(["echo", "blue blue blue"], Tokens(run($"echo {"blue blue blue"}")));
    }

    [Fact]
    public void SequenceExpandsToArguments()
    {
        string[] files = ["a b.txt", "c.txt"];
        Assert.Equal(["rm", "a b.txt", "c.txt"], Tokens(run($"rm {files}")));
        Assert.Equal(["rm", "a", "b"], Tokens(run($"rm {new List<string> { "a", "b" }}")));
        Assert.Equal(["rm", "-f"], Tokens(run($"rm {Array.Empty<string>()} -f")));

        var release = true;
        Assert.Equal(["dotnet", "build", "-c", "Release"], Tokens(run($"dotnet build {(release ? ["-c", "Release"] : [])}")));
    }

    [Fact]
    public void SequenceJoinsFirstAndLastWithAdjacentLiteral()
    {
        string[] items = ["x y", "z"];
        Assert.Equal(["cmd", "prex y", "zpost"], Tokens(run($"cmd pre{items}post")));
    }

    [Fact]
    public void NullIsOmittedAndEmptyIsKept()
    {
        string? nothing = null;
        var empty = "";
        Assert.Equal(["cmd", "a", "", "b"], Tokens(run($"cmd a {nothing} {empty} b")));
    }

    [Fact]
    public void QuotesGroupLiteralText()
    {
        Assert.Equal(["cmd", "a b", "c d", "", "e'f"], Tokens(run($"cmd 'a b' \"c d\" '' \"e'f\"")));
    }

    [Fact]
    public void BackslashIsNotAnEscape()
    {
        Assert.Equal([@"C:\tools\x.exe", @"\\server\share\"], Tokens(run($@"C:\tools\x.exe \\server\share\")));
    }

    [Fact]
    public void ValuesAreFormattedWithInvariantCulture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal(["cmd", "1.5", "42", "FF", "--n=3"], Tokens(run($"cmd {1.5} {42} {255:X} --n={3}")));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public async Task AwaitCanBeUsedInHole()
    {
        Assert.Equal(["dotnet", "--version"], Tokens(run($"dotnet {await Task.FromResult("--version")}")));
    }

    [Fact]
    public void InvalidCommands()
    {
        Assert.Throws<FormatException>(() => run($"cmd 'unterminated"));
        Assert.Throws<ArgumentException>(() => run($"  "));

        string? nothing = null;
        Assert.Throws<ArgumentException>(() => run($"{nothing}"));
    }

    [Fact]
    public void ToStringQuotesForDisplay()
    {
        var message = "a b";
        Assert.Equal("git commit -m \"a b\" \"\"", run($"git commit -m {message} ''").ToString());
        Assert.Equal("a x | b y", (run($"a x") | run($"b y")).ToString());
    }
}
