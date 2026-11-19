using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Xunit;
using ProcessX.Analyzer;

namespace ProcessX.Tests;

public class AnalyzerTest
{
    const string Usings = """
        using System;
        using System.Threading.Tasks;
        using ProcessX;
        using static ProcessX.Zx;

        """;

    static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(x => (MetadataReference)MetadataReference.CreateFromFile(x)),
        MetadataReference.CreateFromFile(typeof(ProcessX.Zx).Assembly.Location),
    ];

    /// <summary>Compiles a script (top-level statements) and returns compiler + analyzer diagnostics, suppressed ones included.</summary>
    static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string script)
    {
        var compilation = CSharpCompilation.Create(
            "Script",
            [CSharpSyntaxTree.ParseText(Usings + script, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, reportSuppressedDiagnostics: true));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new AwaitStringAnalyzer(), new UnawaitedStringSuppressor());
        var options = new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: true);
        var diagnostics = await compilation.WithAnalyzers(analyzers, options).GetAllDiagnosticsAsync();

        var errors = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error && x.Id.StartsWith("CS")).ToArray();
        Assert.True(errors.Length == 0, "script does not compile: " + string.Join(Environment.NewLine, errors.Select(x => x.ToString())));

        return diagnostics;
    }

    static async Task<string[]> AnalyzeIdsAsync(string script, string prefix)
    {
        var diagnostics = await AnalyzeAsync(script);
        return diagnostics.Where(x => x.Id.StartsWith(prefix)).Select(x => x.Id).ToArray();
    }

    [Theory]
    [InlineData("""await "dotnet --version";""")]
    [InlineData("""var v = await "dotnet --version";""")]
    [InlineData("""const string K = "dotnet"; await K;""")]
    [InlineData("""const string K = "dotnet"; await $"{K} --version";""")]
    [InlineData("""const string K = "dotnet"; await (K + " --version");""")]
    [InlineData("""await new[] { "echo 1", "echo 2" };""")]
    [InlineData("""await sh("dotnet --list-sdks | sort");""")]
    [InlineData("""const string K = "dotnet"; await sh(K + " --version");""")]
    [InlineData("""var x = "a"; await sh($"echo {x}");""")]
    [InlineData("""var x = "a | b"; await sh($"echo {x:raw}");""")]
    [InlineData("""await sh($"echo {"constant hole"}");""")]
    [InlineData("""var x = "a"; await run($"echo {x}");""")]
    [InlineData("""var x = "a"; await run(x);""")]
    [InlineData("""await Task.FromResult("not a command");""")]
    [InlineData("""await "cd sub && dotnet build";""")]
    public async Task Allowed(string script)
    {
        Assert.Empty(await AnalyzeIdsAsync(script, "PX"));
    }

    [Theory]
    [InlineData("""var x = "dotnet --version"; await x;""")]
    [InlineData("""var x = "main"; await $"git checkout {x}";""")]
    [InlineData("""var x = "main"; await ("git checkout " + x);""")]
    [InlineData("""var x = "main"; var s = await ("git checkout " + x);""")]
    [InlineData("""await Console.ReadLine()!;""")]
    [InlineData("""var x = "echo 2"; await new[] { "echo 1", x };""")]
    [InlineData("""var x = new[] { "echo 1" }; await x;""")]
    public async Task NonConstantIsError(string script)
    {
        var diagnostics = (await AnalyzeAsync(script)).Where(x => x.Id.StartsWith("PX")).ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PX001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("""var x = "dotnet --version"; await sh(x);""")]
    [InlineData("""var x = "main"; await sh("git checkout " + x);""")]
    [InlineData("""var x = "main"; await sh(("git checkout " + x));""")]
    [InlineData("""var x = "main"; var command = sh(string.Concat("git checkout ", x)); await command;""")]
    [InlineData("""await sh(Console.ReadLine()!);""")]
    public async Task NonConstantShellScriptIsError(string script)
    {
        var diagnostics = (await AnalyzeAsync(script)).Where(x => x.Id.StartsWith("PX")).ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PX003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // the message has no arguments, so braces are shown as written
        Assert.Contains("{value}", diagnostic.GetMessage());
        Assert.DoesNotContain("{{", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("""await sh("cd ..");""")]
    [InlineData("""await "cd ..";""")]
    [InlineData("""await " cd ../../ ";""")]
    [InlineData("""await "chdir sub";""")]
    [InlineData("""await new[] { "echo 1", "cd sub" };""")]
    public async Task ChangeDirectoryOnlyIsWarning(string script)
    {
        var diagnostics = (await AnalyzeAsync(script)).Where(x => x.Id.StartsWith("PX")).ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("PX002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [InlineData("""await Task.Yield(); Console.ReadLine();""")]
    [InlineData("""await Task.Yield(); "abc".Trim();""")]
    [InlineData("""await Task.Yield(); "a b".Split(' ');""")]
    public async Task UnawaitedStringIsSuppressed(string script)
    {
        var diagnostics = (await AnalyzeAsync(script)).Where(x => x.Id == "CS4014").ToArray();

        // the compiler does report it (string is awaitable because of `using ProcessX;`), and the suppressor silences it
        var diagnostic = Assert.Single(diagnostics);
        Assert.True(diagnostic.IsSuppressed);
    }

    [Theory]
    [InlineData("""await Task.Yield(); Task.Delay(1);""")]
    [InlineData("""await Task.Yield(); run($"dotnet --version");""")]
    [InlineData("""await Task.Yield(); sh("dotnet --version");""")]
    public async Task ForgottenAwaitIsStillReported(string script)
    {
        var diagnostics = (await AnalyzeAsync(script)).Where(x => x.Id == "CS4014").ToArray();

        var diagnostic = Assert.Single(diagnostics);
        Assert.False(diagnostic.IsSuppressed);
    }
}
