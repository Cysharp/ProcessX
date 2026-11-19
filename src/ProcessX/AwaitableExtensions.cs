using System.Runtime.CompilerServices;

namespace ProcessX;

/// <summary>
/// Enables <c>await "..."</c> and awaiting several commands at once. Imported by <c>using ProcessX;</c>.
/// </summary>
/// <remarks>
/// The string awaiters run the text through the shell verbatim, so the bundled analyzer (PX001)
/// only accepts compile-time constants. Use <see cref="Zx.run(CommandInterpolatedStringHandler)"/> for values.
/// </remarks>
public static class AwaitableExtensions
{
    /// <summary><c>await "script"</c>: same as <c>await sh("script")</c>, returns stdout.</summary>
    public static TaskAwaiter<string> GetAwaiter(this string script)
    {
        return RunScriptAsync(script).GetAwaiter();
    }

    /// <summary><c>await new[] { "a", "b" }</c>: run scripts in parallel, returns each stdout.</summary>
    public static TaskAwaiter<string[]> GetAwaiter(this string[] scripts)
    {
        return Task.WhenAll(scripts.Select(RunScriptAsync)).GetAwaiter();
    }

    /// <summary>Run commands in parallel.</summary>
    public static TaskAwaiter<CommandResult[]> GetAwaiter(this IEnumerable<Command> commands)
    {
        return Task.WhenAll(commands.Select(x => x.ExecuteAsync())).GetAwaiter();
    }

    /// <summary>Run commands in parallel.</summary>
    public static TaskAwaiter<(CommandResult, CommandResult)> GetAwaiter(this (Command, Command) commands)
    {
        return WhenAll(commands).GetAwaiter();

        static async Task<(CommandResult, CommandResult)> WhenAll((Command, Command) commands)
        {
            var results = await Task.WhenAll(commands.Item1.ExecuteAsync(), commands.Item2.ExecuteAsync()).ConfigureAwait(false);
            return (results[0], results[1]);
        }
    }

    /// <summary>Run commands in parallel.</summary>
    public static TaskAwaiter<(CommandResult, CommandResult, CommandResult)> GetAwaiter(this (Command, Command, Command) commands)
    {
        return WhenAll(commands).GetAwaiter();

        static async Task<(CommandResult, CommandResult, CommandResult)> WhenAll((Command, Command, Command) commands)
        {
            var results = await Task.WhenAll(commands.Item1.ExecuteAsync(), commands.Item2.ExecuteAsync(), commands.Item3.ExecuteAsync()).ConfigureAwait(false);
            return (results[0], results[1], results[2]);
        }
    }

    /// <summary>Run commands in parallel.</summary>
    public static TaskAwaiter<(CommandResult, CommandResult, CommandResult, CommandResult)> GetAwaiter(this (Command, Command, Command, Command) commands)
    {
        return WhenAll(commands).GetAwaiter();

        static async Task<(CommandResult, CommandResult, CommandResult, CommandResult)> WhenAll((Command, Command, Command, Command) commands)
        {
            var results = await Task.WhenAll(commands.Item1.ExecuteAsync(), commands.Item2.ExecuteAsync(), commands.Item3.ExecuteAsync(), commands.Item4.ExecuteAsync()).ConfigureAwait(false);
            return (results[0], results[1], results[2], results[3]);
        }
    }

    static async Task<string> RunScriptAsync(string script)
    {
        // the analyzer (PX001) has made sure that the script is a constant
        var result = await Zx.shell.CreateCommand(script).ExecuteAsync().ConfigureAwait(false);
        return result.StdOut;
    }
}
