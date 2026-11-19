[![GitHub Actions](https://github.com/Cysharp/ProcessX/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/ProcessX/actions)

ProcessX
===

ProcessX v2 lets you write shell scripts in C#, like [google/zx](https://github.com/google/zx). v2 requires .NET 11 and adds easy-to-use features on top of the high-performance [.NET 11 Process API](https://devblogs.microsoft.com/dotnet/process-api-improvements-in-dotnet-11/). It works especially well with file-based apps.

```csharp
#!/usr/bin/env dotnet
#:package ProcessX@2.*
using ProcessX;
using static ProcessX.Zx;

// `await string` runs it through the shell and returns stdout
await "dotnet --list-sdks | sort";

// receive stdout as a string, and pass it to the next command with run($"...")
var branch = await "git branch --show-current";
await run($"git push origin {branch}");

// execute in parallel
var (dotnet, git, msg) = await (
    run("dotnet --version"),
    run("git --version"),
    sh("echo foo")
);
```

Save it as `build.cs` and run it with `dotnet run build.cs` (or `chmod +x build.cs && ./build.cs`).

ProcessX is Native AOT compatible, so `dotnet publish build.cs` turns the script into a single native executable.

Getting Started
---
ProcessX requires `.NET 11` or later.

In a [file-based app](https://learn.microsoft.com/dotnet/core/sdk/file-based-apps), add the package with a directive:

```csharp
#:package ProcessX@2.*
```

In a project, install it from [NuGet](https://www.nuget.org/packages/ProcessX):

```bash
dotnet add package ProcessX
```

Then import the API, `using ProcessX;` and `using static ProcessX.Zx;`.

```csharp
using ProcessX;
using static ProcessX.Zx;
```

Awaiting a command runs the process and gives you its output. There are three ways to write a command: `await "..."`, `await run($"...")` and `await sh($"...")`.

`await run($"...")`: start a process without a shell
---
`run` splits the literal text on whitespace and passes each interpolation hole as it is. A value never needs quoting or escaping because it never meets a shell.

```csharp
using ProcessX;
using static ProcessX.Zx;

var dir = "foo/foo bar";
await run($"mkdir {dir}");                  // mkdir "foo/foo bar" (one argument)

var author = "A B";
await run($"git log --author={author}");    // a hole joins the text around it: "--author=A B"

string[] files = ["a b.txt", "c.txt"];
await run($"rm {files}");                   // a sequence expands to one argument per element

var release = true;
await run($"dotnet build {(release ? ["-c", "Release"] : [])}"); // optional arguments

string? filter = null;
await run($"dotnet test {filter}");         // null adds nothing ("" would add an empty argument)

await run($"git commit -m 'fixed text'");   // quotes group literal text; backslash is an ordinary character
await run("dotnet --version");              // a plain string is split the same way
```

Numbers and other `IFormattable` values are formatted with the invariant culture.

There is no shell, so a glob such as `*.txt` is not expanded. Expand it in C# and pass the sequence:

```csharp
await run($"rm {Directory.GetFiles(".", "*.txt")}");
```

For the same reason `run` can only start an executable. A command that the shell provides needs `sh` (or `await "..."`), and the error message says so when that is the case:

* Shell built-ins. On Windows that includes `echo`, `dir`, `type`, `copy` and `set`, so `run($"echo hello")` works on Linux and macOS but not on Windows.
* Batch files on Windows. Many tools are one: `npm` is `npm.cmd`, `code` is `code.cmd`, `az` is `az.cmd`.

`await "..."`, `await sh($"...")`: run through the shell
---
`await "..."` runs a constant script through the shell and returns stdout as a `string`.

`sh($"...")` runs through the shell too, and like `run` it takes values in `{}`. The text is the script, and each interpolation hole is a value that is quoted for the shell, so it stays one argument and can not be read as script.

```csharp
using ProcessX;
using static ProcessX.Zx;

await "cat package.json | grep name";

var branch = await "git branch --show-current";

var path = "my file.txt & del *";
await sh($"type {path} | findstr TODO");        // cmd.exe gets: type "my file.txt & del *" | findstr TODO

var author = "A B";
await sh($"git log --author={author} | sort");  // a hole joins the text around it, as in run

string[] packages = ["left-pad", "is-odd"];
await sh($"npm install {packages}");            // a sequence is one quoted value per element

string? filter = null;
await sh($"dotnet test {filter} | sort");       // null adds nothing

var redirect = "> sdks.txt";
await sh($"dotnet --list-sdks {redirect:raw}"); // :raw inserts the value as script text, unquoted
```

How a value is quoted depends on the shell:

| Shell | A value is written as | |
| --- | --- | --- |
| bash, sh and custom shells | `'value'`, a quote inside as `'\''` | Any value. |
| pwsh | `'value'`, a quote inside doubled | Any value. |
| cmd.exe | `"value"` | cmd.exe has no way to quote every character: a value that contains `"`, `%` or a line break throws `ArgumentException`. Use `run`, or another shell. |

A string that is not an interpolated string reaches the shell as it is, in `sh` and in `await "..."`. Both only accept a compile-time constant there; anything else is an error ([PX001, PX003](#analyzer)):

```csharp
await $"git checkout {branch}";              // error PX001: await can not quote the value
await sh("git checkout " + branch);          // error PX003: a script built at runtime
await sh($"git checkout {branch} && make");  // OK, branch is quoted
await run($"git checkout {branch}");         // OK, no shell
```

The shell is `cmd.exe` on Windows and `bash` (`/bin/sh` if there is no bash) on others. Change it with `Zx.shell`:

```csharp
shell = Shell.Pwsh;                          // Shell.Cmd, Shell.Bash, Shell.Sh, Shell.Pwsh
shell = new Shell("/bin/zsh", "-c");         // or any command that takes the script as the last argument
```

The built-in shells are set up so that a failure inside the script is not lost, as google/zx does:

| Shell | Added to the script | Effect |
| --- | --- | --- |
| `Shell.Bash` (and the default on Linux/macOS) | `set -euo pipefail;` in front | A failing command, a failing command inside a pipe (`a \| b`), or an unset variable fails the script. |
| `Shell.Sh` | `set -eu;` in front | A failing command or an unset variable fails the script. |
| `Shell.Pwsh` | `$ErrorActionPreference = 'Stop';` in front, `; exit $LASTEXITCODE` after | A cmdlet error stops the script, and the exit code of a native command is passed on. |
| `Shell.Cmd` | nothing | cmd.exe has no such option: `a & b` and `a \| b` report only the last command. |

A shell created with `new Shell(...)` adds nothing unless you say so:

```csharp
shell = new Shell("/bin/zsh", "-c") { Prefix = "set -euo pipefail; " };
shell = new Shell("bash", "-c");             // plain bash, without the prefix
```

A custom shell quotes values like bash unless told otherwise:

```csharp
shell = new Shell("powershell.exe", "-Command") { Quoting = ShellQuoting.PowerShell };
```

Stdout
---
By default, the output of a process is also written to the console: its stdout to stdout, and its stderr to stderr.

```csharp
// this prints the version twice: once by the command itself, once by log
var version = await "dotnet --version";
log(version, ConsoleColor.Green);
```

To turn that off for every command, set `quiet = true`.

```csharp
// Zx.quiet, default is false
quiet = true;

var version = await "dotnet --version";
log(version, ConsoleColor.Green);
```

To turn it off for a single command, call `.Quiet()`. `.Quiet(false)` does the opposite: it writes the output of one command while `quiet` is true.

```csharp
var version = await run("dotnet --version").Quiet();
log(version, ConsoleColor.Green);
```

Result and errors
---
Awaiting a command returns `CommandResult`.

```csharp
var result = await run($"dotnet --list-sdks");

int exitCode = result.ExitCode;
string stdout = result.StdOut;               // lines joined by Environment.NewLine, no trailing newline
string stderr = result.StdErr;
IReadOnlyList<string> lines = result.Lines;  // stdout line by line

string text = await run($"dotnet --version");            // converts to string (stdout) implicitly
var (output, error) = await run($"dotnet --info");       // deconstructs to (stdout, stderr)
```

A non-zero exit code throws `ProcessErrorException`. Output on stderr alone is not an error.

```csharp
try
{
    await run($"dotnet --foo");
}
catch (ProcessErrorException ex)
{
    // int ex.ExitCode, IReadOnlyList<string> ex.ErrorOutput, string ex.CommandLine, CommandResult ex.Result
    log(ex.Message, ConsoleColor.Red);
}

// or handle the exit code yourself
var result = await run($"robocopy {src} {dst}").NoThrow();
if (result.ExitCode >= 8) { /* ... */ }
```

Options
---
`run` and `sh` return a `Command`. It does not start until it is awaited, so options can be chained before that. Awaiting the same `Command` again runs it again.

```csharp
await run($"dotnet test").Timeout(TimeSpan.FromMinutes(5));
```

| Method | |
| --- | --- |
| `NoThrow()` | Do not throw on a non-zero exit code. |
| `Quiet()`, `Quiet(false)` | Do not echo the output of this command to the console. It overrides `Zx.quiet`, so `Quiet(false)` echoes one command when everything else is quiet. |
| `Inherit()` | Do not capture: the process writes to the console directly, so colors, progress bars and prompts work. The result only has an exit code. |
| `Timeout(TimeSpan)`, `Timeout(int seconds)` | Stop the process and throw `TimeoutException`. |
| `WithCancellation(CancellationToken)` | Stop the process and throw `OperationCanceledException`. |
| `GracePeriod(TimeSpan)`, `GracePeriod(int seconds)` | How long a process that was asked to stop may take before it is killed (see [Stopping a process](#stopping-a-process)). Default is 3 seconds. |
| `Cwd(string)` | Working directory of this command. |
| `SetEnv(string name, string? value)` | Environment variable of this command (null removes it). |
| `Input(string)`, `Input(byte[])`, `Input(Stream)` | Write the text, the bytes or the stream to stdin of the process. |
| `WithEncoding(Encoding)` | Encoding of stdout/stderr, and of the text given to `Input`. |
| `ExecuteAsync(CancellationToken)` | Start it and return `Task<CommandResult>`. |
| `ToBytesAsync(CancellationToken)` | Start it and return stdout as `byte[]` (see [Binary output](#binary-output)). |
| `ToStreamAsync(Stream, CancellationToken)` | Start it and copy stdout to the stream. |

Processes do not outlive the script: a stopped command is killed with its whole process tree (a shell command is `shell -> real process`), and on Windows and Linux children are killed when the script itself exits or is killed.

### Stopping a process

A timeout, a cancellation, or leaving an `await foreach` stops the process. `await` returns only after the process has really exited.

Where the process can be asked to stop, it is asked first and gets the grace period to finish by itself; then the process tree is killed.

`terminateToken` (`Zx.terminateToken`) is a `CancellationToken` built into ProcessX that handles Ctrl + C (SIGINT), Ctrl + Break (SIGQUIT) and SIGTERM.

| Situation | What happens |
| --- | --- |
| Ctrl + C, with `terminateToken` | The system has already delivered the signal to the process. ProcessX waits for the grace period, then kills. |
| Timeout or cancellation of a `run` command, on Linux/macOS | SIGTERM is sent. ProcessX waits for the grace period, then kills. |
| Timeout or cancellation on Windows | Killed immediately: Windows can not signal a single process. |
| Timeout or cancellation of a shell command (`sh`, `await "..."`) | Killed immediately: a signaled shell would exit and leave its children running. |

```csharp
// `docker compose up` stops its containers on Ctrl + C; give it time to do that
terminateTimeout = TimeSpan.FromSeconds(40);
await run($"docker compose up").WithCancellation(terminateToken).GracePeriod(30);

// never wait
await run($"dotnet watch").WithCancellation(token).GracePeriod(0);
```

### Standard input

stdin is inherited, so a command can read from the console. `Input` replaces it with a text, bytes (`byte[]` or `ReadOnlyMemory<byte>`) or a stream; the text is written as it is and never appears in the command line, which makes it the way to pass a secret:

```csharp
await run($"docker login -u {user} --password-stdin").Input(password);
await run($"kubectl apply -f -").Input(yaml);
await run($"git apply").Input(File.OpenRead("fix.patch"));
```

Pipe
---
`|` connects stdout of a command to stdin of the next one with an OS pipe. No shell is involved, so holes are still safe.

```csharp
var pattern = "fix:";
var result = await (run($"git log --oneline") | run($"grep {pattern}") | run($"wc -l"));
```

Options of the last command apply to the pipeline. The pipeline fails when any command in it fails, and `ExitCode` is the last non-zero one.

Parallel
---
Await a tuple or a sequence to run commands at once.

```csharp
var (build, lint) = await (run($"dotnet build"), sh("npm run lint"));

CommandResult[] results = await files.Select(x => run($"gzip {x}"));

// constant scripts
await new[]
{
    "echo 1",
    "echo 2",
    "echo 3",
};
```

Streaming
---
`await foreach` reads stdout line by line while the process is running. Leaving the loop stops the process.

```csharp
await foreach (var line in run($"docker logs -f {container}"))
{
    if (line.Contains("ready")) break;
}
```

Binary output
---
Awaiting a command reads stdout as text, line by line. For a command that writes binary data, use `ToBytesAsync` to get stdout byte for byte, or `ToStreamAsync` to copy it somewhere without holding it in memory.

```csharp
byte[] tar = await run($"git archive HEAD").ToBytesAsync();

await using (var file = File.Create("logo.png"))
{
    await run($"curl -sL {url}").ToStreamAsync(file);
}
```

Everything else works the same: options, `Input`, pipes, and `ProcessErrorException` on a non-zero exit code. stderr is still read as text. `ToStreamAsync` returns a `CommandResult` with the exit code and stderr.

`Command` is also an `IAsyncEnumerable<string>`, so `System.Linq` works on the lines: `string[] lines = await run($"...").ToArrayAsync();`.

Zx
---
`ProcessX.Zx` has settings and utility functions, intended to be used via `using static ProcessX.Zx;`.

```csharp
// Zx.quiet, do not echo stdout/stderr of commands to the console. default is false (output is echoed).
quiet = true;

// Zx.shell, the shell of `await "..."` and sh(...).
shell = Shell.Bash;

// Zx.cd(string path), change the current directory of the script (and so of commands).
cd("../../");

// Zx.which(string commandName), find a command in PATH. returns null when not found.
if (which("git") == null) throw new Exception("git is required");

// Zx.terminateToken, CancellationToken that is canceled by Ctrl + C (SIGINT), Ctrl + Break (SIGQUIT) and SIGTERM,
// so that the script can clean up, like `trap` in a shell script.
try
{
    await run($"docker compose up").WithCancellation(terminateToken);
}
finally
{
    await run($"docker compose down");
}

// Zx.terminateTimeout, how long the script may keep running after terminateToken is canceled. default is 5 seconds.
terminateTimeout = TimeSpan.FromSeconds(30);

// Zx.log(object? value, ConsoleColor? color) and Zx.color(ConsoleColor color)
log("red log.", ConsoleColor.Red);
using (color(ConsoleColor.Blue))
{
    log("blue log");
    await run($"dotnet --version"); // the output of a command is blue too
}

// Zx.question(string question), read a line from stdin.
var bear = await question("What kind of bear is best?");

// Zx.sleep(int seconds|TimeSpan timeSpan), wrapper of Task.Delay.
await sleep(5);
```

Without `terminateToken`, Ctrl + C simply ends the script and its commands. Reading `terminateToken` takes over the signal: the script is no longer ended immediately, the token is canceled instead, and if the script is still running after `terminateTimeout` the process exits with code 130. The [grace period](#stopping-a-process) of the commands being stopped is part of that time, so raise `terminateTimeout` along with a long `GracePeriod`.

Commands inherit the environment of the script, so use the BCL for anything that applies to every command: `Environment.SetEnvironmentVariable("NODE_ENV", "production")`. `SetEnv` and `Cwd` are for a single command.

If you want more colors, like Chalk in JavaScript, the [Cysharp/Kokuban](https://github.com/Cysharp/Kokuban) styler for .NET console apps will help, or you can use [Spectre.Console](https://spectreconsole.net/) for richer styling. For command line arguments, [ConsoleAppFramework](https://github.com/Cysharp/ConsoleAppFramework) works in a file-based app as well.

Analyzer
---
The package includes an analyzer.

| ID | Severity | |
| --- | --- | --- |
| PX001 | Error | `await` on a string that is not a compile-time constant. The script goes to the shell without escaping; use `run($"...")` or `sh($"...")` to pass values. |
| PX002 | Warning | `await "cd path"` or `sh("cd path")` only changes the directory of the shell process that runs it. Use `cd("path")`. (`cd path && command` is fine.) |
| PX003 | Error | A string that is not a compile-time constant passed to `sh(...)`. Use `sh($"...{value}...")` so that values are quoted, or `{value:raw}` to insert script text on purpose. |

`using ProcessX;` makes `string` awaitable, and that has a side effect: the compiler reports CS4014 ("this call is not awaited") for any discarded call that returns a string, such as `Console.ReadLine();`. The analyzer suppresses CS4014 for `string` and `string[]`. It still reports a `Command` that was not awaited, which is useful because a `Command` does nothing until it is awaited.

Without `using ProcessX;` strings are not awaitable at all, and `using static ProcessX.Zx;` alone is enough for `run`, `sh`, their options and `await foreach`. It is not a full replacement, though: awaiting a tuple or a sequence of commands also needs `using ProcessX;`, and types such as `Shell` and `ProcessErrorException` then have to be written with the namespace.

Migration from v1
---
For .NET Standard 2.0 and the old API, use [v1](https://github.com/Cysharp/ProcessX/tree/1.5.6). Here is the guide from v1 to v2.

* The namespace is `ProcessX` instead of `Zx` and `Cysharp.Diagnostics`: replace `using Zx;` with `using ProcessX;` and `using static Zx.Env;` with `using static ProcessX.Zx;`.
* The static class `Cysharp.Diagnostics.ProcessX` (`StartAsync`, `GetDualAsyncEnumerable`, `StartReadBinaryAsync`) is removed. Use the .NET 11 `Process` API, or stay on v1.
* `ProcessErrorException` moved to the `ProcessX` namespace. `AcceptableExitCodes` is replaced by `NoThrow()`.
* `await "..."` only accepts constants, and `await "cd path"` no longer changes the directory; use `cd(path)`.
* `verbose` is replaced by `quiet` with the opposite meaning: `verbose = false` becomes `quiet = true`.
* `useShell` is removed: `run` never uses a shell, `await "..."` and `sh` always do. `shell` is a `Shell` instead of a string.
* `workingDirectory` and `envVars` are removed: use `cd(path)` and `Environment.SetEnvironmentVariable`, or `Cwd` / `SetEnv` for a single command.
* `fetch`, `fetchText` and `fetchBytes` are removed: use `HttpClient`.
* `run($"...")` passes holes as arguments instead of quoting them into a shell command line, and returns a `Command` instead of `Task<string>`.

| v1 | v2 |
| --- | --- |
| `run2($"...")`, `process2("...")` | `var (stdout, stderr) = await run($"...")` |
| `runl($"...")`, `processl("...")` | `(await run($"...")).Lines` |
| `process("...")` | `sh("...")` for a constant, `sh($"...")` with values |
| `withTimeout($"...", 10)` | `run($"...").Timeout(10)` |
| `withCancellation($"...", token)` | `run($"...").WithCancellation(token)` |
| `ignore(run($"..."))` | `run($"...").NoThrow()` |
| `escape($"...")` | not needed |

License
---
This library is under the MIT License.
