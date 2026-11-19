// ProcessX: write shell scripts in C#, like google/zx.

using ProcessX;
using System.Xml.Linq;
using static ProcessX.Zx;



// run($"...") starts the process directly; each hole is passed as one argument, whatever it contains
var message = "hello \"world\" & more";
await run($"dotnet --version {Array.Empty<string>()}");
log(run($"git commit -m {message}")); // prints the command line, nothing runs until awaited

// result has more than stdout
var result = await run($"dotnet --list-sdks").Quiet();
log($"{result.Lines.Count} SDKs, exit code {result.ExitCode}");

// non-zero exit code throws, unless NoThrow
try
{
    await run($"dotnet --no-such-option").Quiet();
}
catch (ProcessErrorException ex)
{
    log($"failed with {ex.ExitCode}", ConsoleColor.Red);
}

var failed = await run($"dotnet --no-such-option").Quiet().NoThrow();
log($"exit code {failed.ExitCode}");

// parallel
var (sdks, runtimes) = await (run($"dotnet --list-sdks").Quiet(), run($"dotnet --list-runtimes").Quiet());
log($"{sdks.Lines.Count} SDKs, {runtimes.Lines.Count} runtimes");

await new[]
{
    "echo 1",
    "echo 2",
    "echo 3",
};

// OS pipe between two processes, no shell
var piped = await (run($"dotnet --list-sdks") | run($"dotnet --version")).Quiet();
log(piped);

// streaming
await foreach (var line in run($"dotnet --list-runtimes"))
{
    log("> " + line, ConsoleColor.DarkGray);
}

// timeout kills the whole process tree
try
{
    await (OperatingSystem.IsWindows() ? sh("ping -n 30 127.0.0.1") : sh("sleep 30")).Timeout(1).Quiet();
}
catch (TimeoutException ex)
{
    log(ex.Message, ConsoleColor.Yellow);
}

using (color(ConsoleColor.Blue))
{
    log("blue log");
    await run($"dotnet --version");
}
