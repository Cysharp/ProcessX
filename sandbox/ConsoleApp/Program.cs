using Cysharp.Diagnostics; // using namespace
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

class Program
{

    static async Task Main(string[] args)
    {
        await ProcessX.StartAsync("dotnet --info").WriteLineAllAsync();

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
    }
}