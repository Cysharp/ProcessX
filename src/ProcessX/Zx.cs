namespace ProcessX;

/// <summary>
/// Functions and settings for scripts. Intended to be used with <c>using static ProcessX.Zx;</c>.
/// </summary>
public static class Zx
{
    static readonly Lazy<TerminateSignal> terminateSignal = new(() => new TerminateSignal());

    /// <summary>
    /// Do not echo stdout/stderr of commands to the console. Default is false (output is echoed).
    /// <see cref="Command.Quiet"/> does the same for a single command.
    /// </summary>
    public static bool quiet { get; set; }

    /// <summary>The shell used by <see cref="sh(ShellInterpolatedStringHandler)"/> and <c>await "..."</c>.</summary>
    public static Shell shell { get; set; } = Shell.Default;

    /// <summary>
    /// Canceled by Ctrl+C (SIGINT), Ctrl+Break (SIGQUIT) and SIGTERM, so that the script can clean up.
    /// Reading this property takes over those signals: they no longer terminate the script immediately,
    /// and the script has <see cref="terminateTimeout"/> to finish before the process exits with code 130.
    /// </summary>
    public static CancellationToken terminateToken => terminateSignal.Value.Token;

    /// <summary>
    /// How long the script may keep running after <see cref="terminateToken"/> is canceled. Default is 5 seconds.
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables the forced exit.
    /// </summary>
    public static TimeSpan terminateTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Run a process without a shell. Text is split on whitespace, and each interpolation hole is passed
    /// as it is (never split, never escaped): <c>run($"git commit -m {message}")</c>.
    /// A string sequence in a hole expands to one argument per element.
    /// A plain string is also accepted and split the same way: <c>run("dotnet --version")</c>.
    /// </summary>
    // There is deliberately no run(string) overload: C# would pick it for an interpolated string whose holes
    // are all constants, and a constant hole would then be split on whitespace. Plain strings come in through
    // the implicit conversion on the handler instead.
    public static Command run(CommandInterpolatedStringHandler command)
    {
        var (fileName, arguments) = command.Build();
        return new Command(fileName, arguments, null);
    }

    /// <summary>
    /// Run a script through the shell (<see cref="shell"/>), so pipes, redirects and globs work.
    /// The text of the script is passed as it is, and each interpolation hole is quoted for the shell,
    /// so a value can not break out of its argument: <c>sh($"type {path} | findstr TODO")</c>.
    /// <c>{value:raw}</c> inserts a value as script text instead.
    /// </summary>
    /// <remarks>
    /// A plain string is script text. The bundled analyzer (PX003) only accepts a compile-time constant there,
    /// because a script built at runtime bypasses the quoting.
    /// cmd.exe can not quote every value: one that contains a double quote, a percent sign or a line break
    /// throws <see cref="ArgumentException"/>.
    /// </remarks>
    // No sh(string) overload, for the same reason as run: a constant hole must still be quoted.
    public static Command sh(ShellInterpolatedStringHandler script)
    {
        return shell.CreateCommand(script);
    }

    /// <summary>Change the current directory of this process, which commands inherit.</summary>
    public static void cd(string path)
    {
        Environment.CurrentDirectory = Path.GetFullPath(path);
    }

    /// <summary>Find a command in PATH. Returns null when it is not found.</summary>
    public static string? which(string commandName)
    {
        return Which.TryGetPath(commandName, out var path) ? path : null;
    }

    public static Task sleep(int seconds, CancellationToken cancellationToken = default)
    {
        return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    public static Task sleep(TimeSpan timeSpan, CancellationToken cancellationToken = default)
    {
        return Task.Delay(timeSpan, cancellationToken);
    }

    public static async Task<string> question(string question)
    {
        Console.WriteLine(question);
        var str = await Console.In.ReadLineAsync().ConfigureAwait(false);
        return str ?? "";
    }

    public static void log(object? value, ConsoleColor? color = default)
    {
        if (color != null)
        {
            using (Zx.color(color.Value))
            {
                Console.WriteLine(value);
            }
        }
        else
        {
            Console.WriteLine(value);
        }
    }

    public static IDisposable color(ConsoleColor color)
    {
        var current = Console.ForegroundColor;
        Console.ForegroundColor = color;
        return new ColorScope(current);
    }

    sealed class ColorScope(ConsoleColor color) : IDisposable
    {
        public void Dispose()
        {
            Console.ForegroundColor = color;
        }
    }
}
