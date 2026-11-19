namespace ProcessX;

/// <summary>Captured result of a finished command.</summary>
public sealed class CommandResult
{
    static readonly string[] Empty = [];

    readonly string[] stdOutLines;
    readonly string[] stdErrLines;
    string? stdOut;
    string? stdErr;

    internal CommandResult(int exitCode, string[] stdOutLines, string[] stdErrLines)
    {
        this.ExitCode = exitCode;
        this.stdOutLines = stdOutLines;
        this.stdErrLines = stdErrLines;
    }

    internal static CommandResult NoOutput(int exitCode) => new(exitCode, Empty, Empty);

    public int ExitCode { get; }

    /// <summary>Standard output, lines joined by <see cref="Environment.NewLine"/> without a trailing newline.</summary>
    public string StdOut => stdOut ??= string.Join(Environment.NewLine, stdOutLines);

    /// <summary>Standard error, lines joined by <see cref="Environment.NewLine"/> without a trailing newline.</summary>
    public string StdErr => stdErr ??= string.Join(Environment.NewLine, stdErrLines);

    /// <summary>Standard output, line by line.</summary>
    public IReadOnlyList<string> Lines => stdOutLines;

    /// <summary>Standard error, line by line.</summary>
    public IReadOnlyList<string> ErrorLines => stdErrLines;

    public void Deconstruct(out string stdOut, out string stdErr)
    {
        stdOut = StdOut;
        stdErr = StdErr;
    }

    public override string ToString() => StdOut;

    public static implicit operator string(CommandResult result) => result.StdOut;
}
