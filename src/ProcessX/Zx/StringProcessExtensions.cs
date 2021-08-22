using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

        static Task<string> ProcessCommand(string command)
        {
            if (TryChangeDirectory(command))
            {
                return Task.FromResult("");
            }

            return Env.process(command);
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