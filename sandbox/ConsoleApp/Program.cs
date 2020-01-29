using Cysharp.Diagnostics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await foreach (var item in ProcessX.StartAsync("dotnet build --help"))
            {
                Console.WriteLine(item);
            }
        }
    }
}
