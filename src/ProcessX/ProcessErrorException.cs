namespace ProcessX;

/// <summary>Thrown when a command exits with a non-zero exit code (unless <see cref="Command.NoThrow"/> is used).</summary>
public class ProcessErrorException : Exception
{
    public ProcessErrorException(string command, CommandResult result)
        : base(CreateMessage(command, result))
    {
        this.CommandLine = command;
        this.Result = result;
    }

    /// <summary>The command line, formatted for display.</summary>
    public string CommandLine { get; }

    public CommandResult Result { get; }

    public int ExitCode => Result.ExitCode;

    public IReadOnlyList<string> ErrorOutput => Result.ErrorLines;

    static string CreateMessage(string command, CommandResult result)
    {
        var message = "Process returns error, ExitCode:" + result.ExitCode + ", Command:" + command;
        return result.ErrorLines.Count == 0
            ? message
            : message + Environment.NewLine + result.StdErr;
    }
}
