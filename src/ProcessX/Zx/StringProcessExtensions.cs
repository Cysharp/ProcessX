using Cysharp.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Zx
{
    public static class StringProcessExtensions
    {
        public static TaskAwaiter<string> GetAwaiter(this string command)
        {
            return ProcessCommand(command).GetAwaiter();
        }

        public static TaskAwaiter GetAwaiter(this string[] commands)
        {
            async Task ProcessCommands()
            {
                await Task.WhenAll(commands.Select(ProcessCommand));
            }

            return ProcessCommands().GetAwaiter();
        }

        public static TaskAwaiter GetAwaiter(this string[][] commands)
        {
            static async Task ProcessSequential(string[] commands)
            {
                foreach (var cmd in commands)
                {
                    await ProcessCommand(cmd);
                }
            }

            static async Task ProcessParallel(string[][] commands)
            {
                await Task.WhenAll(commands.Select(ProcessSequential));
            }

            return ProcessParallel(commands).GetAwaiter();
        }

        static Task<string> ProcessCommand(string command)
        {
            if (TryChangeDirectory(command))
            {
                return Task.FromResult("");
            }

            var cmd = command;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                cmd = "cmd /c " + command;
            }
            return WriteLineAndReturnString(ProcessX.StartAsync(cmd));
        }

        static async Task<string> WriteLineAndReturnString(ProcessAsyncEnumerable process)
        {
            var sb = new StringBuilder();
            await foreach (var item in process.ConfigureAwait(false))
            {
                sb.AppendLine(item);
                Console.WriteLine(item);
            }
            return sb.ToString();
        }

        static bool TryChangeDirectory(string command)
        {
            if (command.StartsWith("cd ") || command.StartsWith("chdir "))
            {
                var path = Regex.Replace(command, "^cd|^chdir", "").Trim();
                Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, path);
                return true;
            }

            return false;
        }
    }
}