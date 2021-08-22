using Cysharp.Diagnostics;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
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
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _shell = "cmd /c";
                    }
                    else
                    {
                        _shell = ProcessStartAsync("which bash", CancellationToken.None, forceSilcent: true).Result + " -c";
                    }
                }
                return _shell;
            }
            set
            {
                _shell = value;
            }
        }

        static readonly Lazy<CancellationTokenSource> _terminateTokenSource = new Lazy<CancellationTokenSource>(() =>
        {
            var source = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => source.Cancel();
            return source;
        });

        public static CancellationToken terminateToken => _terminateTokenSource.Value.Token;

        public static string? workingDirectory { get; set; }

        static readonly Lazy<IDictionary<string, string>> _envVars = new Lazy<IDictionary<string, string>>(() =>
        {
            return new Dictionary<string, string>();
        });

        public static IDictionary<string, string> envVars => _envVars.Value;

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

        public static Task<string> run(FormattableString command, CancellationToken cancellationToken = default)
        {
            return process(EscapeFormattableString.Escape(command), cancellationToken);
        }

        public static string escape(FormattableString command)
        {
            return EscapeFormattableString.Escape(command);
        }

        public static Task<string> process(string command, CancellationToken cancellationToken = default)
        {
            return ProcessStartAsync(command, cancellationToken);
        }

        public static async Task<string> question(string question)
        {
            Console.WriteLine(question);
            var str = await Console.In.ReadLineAsync();
            return str ?? "";
        }

        public static void log(object? value, ConsoleColor? color = default)
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

        static async Task<string> ProcessStartAsync(string command, CancellationToken cancellationToken, bool forceSilcent = false)
        {
            var cmd = shell + " " + command;
            var sb = new StringBuilder();
            await foreach (var item in ProcessX.StartAsync(cmd, workingDirectory, envVars).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                sb.AppendLine(item);

                if (verbose && !forceSilcent)
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
