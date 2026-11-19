namespace ProcessX;

/// <summary>The shell used by <see cref="Zx.sh(ShellInterpolatedStringHandler)"/> and <c>await "..."</c>.</summary>
public sealed class Shell
{
    const string ShPrefix = "set -eu; ";
    const string BashPrefix = "set -euo pipefail; ";

    readonly string fileName;
    readonly string[] arguments;
    readonly bool cmdStyle;

    /// <summary>Runs <paramref name="fileName"/> with <paramref name="arguments"/> followed by the script as the last argument.</summary>
    public Shell(string fileName, params string[] arguments)
        : this(fileName, arguments, false)
    {
    }

    Shell(string fileName, string[] arguments, bool cmdStyle)
    {
        this.fileName = fileName;
        this.arguments = arguments;
        this.cmdStyle = cmdStyle;
    }

    /// <summary>Text put in front of every script, for example <c>set -euo pipefail;</c>.</summary>
    public string Prefix { get; init; } = "";

    /// <summary>Text put after every script, for example <c>; exit $LASTEXITCODE</c>.</summary>
    public string Postfix { get; init; } = "";

    /// <summary>How the values of <c>sh($"... {value} ...")</c> are quoted. Default is <see cref="ShellQuoting.Posix"/>.</summary>
    public ShellQuoting Quoting { get; init; } = ShellQuoting.Posix;

    /// <summary>cmd.exe /d /s /v:off /c "script"</summary>
    public static Shell Cmd { get; } = new Shell("cmd.exe", [], cmdStyle: true) { Quoting = ShellQuoting.Cmd };

    /// <summary>/bin/sh -c script, with <c>set -eu;</c> so that a failing command or an unset variable fails the script.</summary>
    public static Shell Sh { get; } = new Shell("/bin/sh", "-c") { Prefix = ShPrefix };

    /// <summary>
    /// bash -c script, with <c>set -euo pipefail;</c> (as google/zx does) so that a failing command,
    /// a failing command inside a pipe, or an unset variable fails the script.
    /// </summary>
    public static Shell Bash { get; } = new Shell("bash", "-c") { Prefix = BashPrefix };

    /// <summary>
    /// pwsh -NoProfile -NonInteractive -Command script, set up so that a failure fails the script:
    /// cmdlet errors stop it, and the exit code of a native command is passed on.
    /// </summary>
    public static Shell Pwsh { get; } = new Shell("pwsh", "-NoProfile", "-NonInteractive", "-Command")
    {
        Prefix = "$ErrorActionPreference = 'Stop'; ",
        Postfix = "; exit $LASTEXITCODE",
        Quoting = ShellQuoting.PowerShell,
    };

    /// <summary>Windows: <see cref="Cmd"/>. Others: bash when it is on PATH, otherwise <see cref="Sh"/>.</summary>
    public static Shell Default { get; } = CreateDefault();

    static Shell CreateDefault()
    {
        if (OperatingSystem.IsWindows()) return Cmd;
        return Which.TryGetPath("bash", out var bash) ? new Shell(bash, "-c") { Prefix = BashPrefix } : Sh;
    }

    internal Command CreateCommand(ShellInterpolatedStringHandler script)
    {
        return CreateCommand(script.Build(Quoting));
    }

    internal Command CreateCommand(string script)
    {
        script = Prefix + script + Postfix;

        if (cmdStyle)
        {
            // cmd.exe does not follow the C runtime quoting rules that ArgumentList produces.
            // With /s, cmd strips only the outermost quotes and takes everything in between verbatim.
            // /v:off keeps `!` literal whatever the registry says about delayed expansion.
            return new Command(fileName, [], "/d /s /v:off /c \"" + script + "\"", viaShell: true);
        }

        return new Command(fileName, [.. arguments, script], null, viaShell: true);
    }
}
