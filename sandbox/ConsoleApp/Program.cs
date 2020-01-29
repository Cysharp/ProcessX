using Cysharp.Diagnostics; // using namespace
using System;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        using (var tcs = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
        {
            await foreach (var item in ProcessX.StartAsync("dotnet --info").WithCancellation(tcs.Token))
            {
                Console.WriteLine(item);
            }
        }

    }
}