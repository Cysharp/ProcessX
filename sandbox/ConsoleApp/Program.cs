using Cysharp.Diagnostics; // using namespace
using System;
using System.Threading.Tasks;

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