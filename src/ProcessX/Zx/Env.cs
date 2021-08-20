using Cysharp.Diagnostics;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Zx
{
    public static class Env
    {
        public static bool verbose { get; set; } = true;

        static string? _shell;
        public static string shell
        {
            get
            {
                if (_shell == null)
                {
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        _shell = "cmd /c";
                    }
                    else
                    {
                        _shell = "bash -c";
                    }
                }
                return _shell;
            }
            set
            {
                _shell = value;
            }
        }

        static CancellationTokenSource? terminateTokenSource;
        public static CancellationToken terminateToken
        {
            get
            {
                if (terminateTokenSource == null)
                {
                    terminateTokenSource = new CancellationTokenSource();
                    Console.CancelKeyPress += Console_CancelKeyPress;
                }

                return terminateTokenSource.Token;
            }
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            terminateTokenSource?.Cancel();
        }

        public static Task<HttpResponseMessage> fetch(string requestUri)
        {
            return new HttpClient().GetAsync(requestUri);
        }

        public static Task<string> fetchText(string requestUri)
        {
            return new HttpClient().GetStringAsync(requestUri);
        }

        public static Task<byte[]> fetchBytes(string requestUri)
        {
            return new HttpClient().GetByteArrayAsync(requestUri);
        }

        public static Task sleep(int seconds, CancellationToken cancellationToken = default)
        {
            return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }

        public static Task sleep(TimeSpan timeSpan, CancellationToken cancellationToken = default)
        {
            return Task.Delay(timeSpan, cancellationToken);
        }

        public static async Task<string> withTimeout(string command, int seconds)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            {
                return await ProcessStartAsync(command, cts.Token);
            }
        }

        public static async Task<string> withTimeout(string command, TimeSpan timeSpan)
        {
            using (var cts = new CancellationTokenSource(timeSpan))
            {
                return await ProcessStartAsync(command, cts.Token);
            }
        }

        public static async Task<string> withCancellation(string command, CancellationToken cancellationToken)
        {
            return await ProcessStartAsync(command, cancellationToken);
        }

        public static Task<string> process(string command, CancellationToken cancellationToken = default)
        {
            return ProcessStartAsync(command, cancellationToken);
        }

        public static Task<string> question(string question)
        {
            Console.WriteLine(question);
            return Console.In.ReadLineAsync();
        }

        public static void log(string value, ConsoleColor? color = default)
        {
            if (color != null)
            {
                using (Env.color(color.Value))
                {
                    Console.WriteLine(value);
                }
            }
            else
            {
                Console.WriteLine(value);
            }
        }

        public static IDisposable color(ConsoleColor color)
        {
            var current = Console.ForegroundColor;
            Console.ForegroundColor = color;
            return new ColorScope(current);
        }

        static async Task<string> ProcessStartAsync(string command, CancellationToken cancellationToken)
        {
            var cmd = Env.shell + " " + command;
            var sb = new StringBuilder();
            await foreach (var item in ProcessX.StartAsync(cmd).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                sb.AppendLine(item);

                if (Env.verbose)
                {
                    Console.WriteLine(item);
                }
            }
            return sb.ToString();
        }

        class ColorScope : IDisposable
        {
            readonly ConsoleColor color;

            public ColorScope(ConsoleColor color)
            {
                this.color = color;
            }

            public void Dispose()
            {
                Console.ForegroundColor = color;
            }
        }
    }
}
