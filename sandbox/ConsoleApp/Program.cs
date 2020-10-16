using Cysharp.Diagnostics; // using namespace
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

class Program
{

    static async Task Main(string[] args)
    {
        var path = @"..\..\..\..\ReturnMessage\ReturnMessage.csproj";
        await ProcessX.StartAsync($"dotnet run --project {path} -- str -m foo -c 10").WriteLineAllAsync();

        //var bin = await ProcessX.StartReadBinaryAsync($"dotnet run --project {path} -- bin -s 999 -c 10 -w 10");

        //// first argument is Process, if you want to know ProcessID, use StandardInput, use it.
        //var (_, stdOut, stdError) = ProcessX.GetDualAsyncEnumerable("dotnet --foo --bar");

        //var consumeStdOut = Task.Run(async () =>
        //{
        //    await foreach (var item in stdOut)
        //    {
        //        Console.WriteLine("STDOUT: " + item);
        //    }
        //});

        //var errorBuffered = new List<string>();
        //var consumeStdError = Task.Run(async () =>
        //{
        //    await foreach (var item in stdError)
        //    {
        //        Console.WriteLine("STDERROR: " + item);
        //        errorBuffered.Add(item);
        //    }
        //});

        //try
        //{
        //    await Task.WhenAll(consumeStdOut, consumeStdError);
        //}
        //catch (ProcessErrorException ex)
        //{
        //    // stdout iterator throws exception when exitcode is not 0.
        //    Console.WriteLine("ERROR, ExitCode: " + ex.ExitCode);

        //    // ex.ErrorOutput is empty, if you want to use it, buffer yourself.
        //    // Console.WriteLine(string.Join(Environment.NewLine, errorBuffered));
        //}
        //Console.WriteLine(bin.Length);

        Console.WriteLine(IsInvalidExitCode(0));
        AcceptableExitCodes = new[] { 1 };
        Console.WriteLine(IsInvalidExitCode(0));
        Console.WriteLine(IsInvalidExitCode(1));
        AcceptableExitCodes = new[] { 0, 1 };
        Console.WriteLine(IsInvalidExitCode(0));
        Console.WriteLine(IsInvalidExitCode(1));
    }

    public static IReadOnlyList<int> AcceptableExitCodes { get; set; } = new[] { 0 };

    static bool IsInvalidExitCode(int processExitCode)
    {
        return !AcceptableExitCodes.Any(x => x == processExitCode);
    }
}