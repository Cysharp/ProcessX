[![GitHub Actions](https://github.com/Cysharp/ProcessX/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/ProcessX/actions)

ProcessX
===

ProcessX simplifies call an external process with the aync streams in C# 8.0 without complex `Process` code. You can receive standard output results by `await foreach`, it is completely asynchronous and realtime.

![image](https://user-images.githubusercontent.com/46207/73369038-504f0c80-42f5-11ea-8b36-5c5c979ac882.png)

<!-- START doctoc generated TOC please keep comment here to allow auto update -->
<!-- DON'T EDIT THIS SECTION, INSTEAD RE-RUN doctoc TO UPDATE -->
## Table of Contents

- [Getting Started](#getting-started)
- [Cancellation](#cancellation)
- [Raw Process/StdError Stream](#raw-processstderror-stream)
- [Read Binary Data](#read-binary-data)
- [Change acceptable exit codes](#change-acceptable-exit-codes)
- [Reference](#reference)
- [Competitor](#competitor)
- [License](#license)

<!-- END doctoc generated TOC please keep comment here to allow auto update -->

Getting Started
---
Install library from NuGet that support from `.NET Standard 2.0`.

> PM> Install-Package [ProcessX](https://www.nuget.org/packages/ProcessX)

Main API is only `Cysharp.Diagnostics.ProcessX.StartAsync` and throws `ProcessErrorException` when error detected.

* **Simple**, only write single string command like the shell script.
* **Asynchronous**, by C# 8.0 async streams.
* **Manage Error**, handling exitcode and stderror.

```csharp
using Cysharp.Diagnostics; // using namespace

// async iterate.
await foreach (string item in ProcessX.StartAsync("dotnet --info"))
{
    Console.WriteLine(item);
}

// receive buffered result(similar as WaitForExit).
string[] result = await ProcessX.StartAsync("dotnet --info").ToTask();

// like the shell exec, write all data to console.
await ProcessX.StartAsync("dotnet --info").WriteLineAllAsync();

// when ExitCode is not 0 or StandardError is exists, throws ProcessErrorException
try
{
    await foreach (var item in ProcessX.StartAsync("dotnet --foo --bar")) { }
}
catch (ProcessErrorException ex)
{
    // int .ExitCode
    // string[] .ErrorOutput
    Console.WriteLine(ex.ToString());
}
```

Cancellation
---
to Cancel, you can use `WithCancellation` of IAsyncEnumerable.

```csharp
// when cancel has been called and process still exists, call process kill before exit.
await foreach (var item in ProcessX.StartAsync("dotnet --info").WithCancellation(cancellationToken))
{
    Console.WriteLine(item);
}
```

timeout, you can use `CancellationTokenSource(delay)`.

```csharp
using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
{
    await foreach (var item in ProcessX.StartAsync("dotnet --info").WithCancellation(cts.Token))
    {
        Console.WriteLine(item);
    }
}
```

Raw Process/StdError Stream
---
In default, when stdError is used, buffering error messages and throws `ProcessErrorException` with error messages after process exited. If you want to use stdError in streaming or avoid throws error when process using stderror as progress, diagnostics, you can use `GetDualAsyncEnumerable` method. Also `GetDualAsyncEnumerable` can get raw `Process`, you can use `ProcessID`, `StandardInput` etc.

```csharp
// first argument is Process, if you want to know ProcessID, use StandardInput, use it.
var (_, stdOut, stdError) = ProcessX.GetDualAsyncEnumerable("dotnet --foo --bar");

var consumeStdOut = Task.Run(async () =>
{
    await foreach (var item in stdOut)
    {
        Console.WriteLine("STDOUT: " + item);
    }
});

var errorBuffered = new List<string>();
var consumeStdError = Task.Run(async () =>
{
    await foreach (var item in stdError)
    {
        Console.WriteLine("STDERROR: " + item);
        errorBuffered.Add(item);
    }
});

try
{
    await Task.WhenAll(consumeStdOut, consumeStdError);
}
catch (ProcessErrorException ex)
{
    // stdout iterator throws exception when exitcode is not 0.
    Console.WriteLine("ERROR, ExitCode: " + ex.ExitCode);

    // ex.ErrorOutput is empty, if you want to use it, buffer yourself.
    // Console.WriteLine(string.Join(Environment.NewLine, errorBuffered));
}
```

Read Binary Data
---
If stdout is binary data, you can use `StartReadBinaryAsync` to read `byte[]`.

```csharp
byte[] bin = await ProcessX.StartReadBinaryAsync($"...");
```

Change acceptable exit codes
---
In default, ExitCode is not 0 throws ProcessErrorException. You can change acceptable exit codes globally by `ProcessX.AcceptableExitCodes` property. Default is `[0]`.

Zx
---
like the [google/zx](https://github.com/google/zx), you can write shell script in C#.

```csharp
// ProcessX and C# 9.0 Top level statement; like google/zx.
// You can execute script by "dotnet run"

using Zx;

await $"cat package.json | grep name";

var branch = await "git branch --show-current";

await $"dep deploy --branch={branch}";

// WhenAll
await new[]
{
    new[]{"echo 1" },
    new[]{"echo 2" },
    new[]{"echo 3" },
};

// WhenAll in sequential command.
await new[]
{
    new[]{"sleep 1", "echo 1" },
    new[]{"sleep 2", "echo 2" },
    new[]{"sleep 3", "echo 3" },
};

var name = "foo bar";
await $"mkdir /foo/{name}";
```

Reference
---
`ProcessX.StartAsync` overloads, you can set workingDirectory, environmentVariable, encoding.

```csharp
// return ProcessAsyncEnumerable
StartAsync(string command, string? workingDirectory = null, IDictionary<string, string>? environmentVariable = null, Encoding? encoding = null)
StartAsync(string fileName, string? arguments, string? workingDirectory = null, IDictionary<string, string>? environmentVariable = null, Encoding? encoding = null)
StartAsync(ProcessStartInfo processStartInfo)

// return (Process, ProcessAsyncEnumerable, ProcessAsyncEnumerable)
GetDualAsyncEnumerable(string command, string? workingDirectory = null, IDictionary<string, string>? environmentVariable = null, Encoding? encoding = null)
GetDualAsyncEnumerable(string fileName, string? arguments, string? workingDirectory = null, IDictionary<string, string>? environmentVariable = null, Encoding? encoding = null)
GetDualAsyncEnumerable(ProcessStartInfo processStartInfo)

// return Task<byte[]>
StartReadBinaryAsync(string command, string? workingDirectory = null, IDictionary<string, string>? environmentVariable = null, Encoding? encoding = null)
StartReadBinaryAsync(string fileName, string? arguments, string? workingDirectory = null, IDictionary<string, string>? environmentVariable = null, Encoding? encoding = null)
StartReadBinaryAsync(ProcessStartInfo processStartInfo)

// return Task<string[]>
ToTask(CancellationToken cancellationToken = default)

// return Task
WriteLineAllAsync(CancellationToken cancellationToken = default)
```

Competitor
---
* [Tyrrrz/CliWrap](https://github.com/Tyrrrz/CliWrap) - Wrapper for command line interfaces.
* [jamesmanning/RunProcessAsTask](https://github.com/jamesmanning/RunProcessAsTask) - Simple wrapper around System.Diagnostics.Process to expose it as a System.Threading.Tasks.Task.

License
---
This library is under the MIT License.
