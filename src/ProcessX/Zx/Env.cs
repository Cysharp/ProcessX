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

        public static async Task<string> withTimeout(FormattableString command, int seconds)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            {
                return (await ProcessStartAsync(EscapeFormattableString.Escape(command), cts.Token)).StdOut;
            }
        }

        public static async Task<(string StdOut, string StdError)> withTimeout2(FormattableString command, int seconds)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            {
                return (await ProcessStartAsync(EscapeFormattableString.Escape(command), cts.Token));
            }
        }

        public static async Task<string> withTimeout(FormattableString command, TimeSpan timeSpan)
        {
            using (var cts = new CancellationTokenSource(timeSpan))
            {
                return (await ProcessStartAsync(EscapeFormattableString.Escape(command), cts.Token)).StdOut;
            }
        }

        public static async Task<(string StdOut, string StdError)> withTimeout2(FormattableString command, TimeSpan timeSpan)
        {
            using (var cts = new CancellationTokenSource(timeSpan))
            {
                return (await ProcessStartAsync(EscapeFormattableString.Escape(command), cts.Token));
            }
        }

        public static async Task<string> withCancellation(FormattableString command, CancellationToken cancellationToken)
        {
            return (await ProcessStartAsync(EscapeFormattableString.Escape(command), cancellationToken)).StdOut;
        }

        public static async Task<(string StdOut, string StdError)> withCancellation2(FormattableString command, CancellationToken cancellationToken)
        {
            return (await ProcessStartAsync(EscapeFormattableString.Escape(command), cancellationToken));
        }

        public static Task<string> run(FormattableString command, CancellationToken cancellationToken = default)
        {
            return process(EscapeFormattableString.Escape(command), cancellationToken);
        }

        public static Task<(string StdOut, string StdError)> run2(FormattableString command, CancellationToken cancellationToken = default)
        {
            return process2(EscapeFormattableString.Escape(command), cancellationToken);
        }

        public static string escape(FormattableString command)
        {
            return EscapeFormattableString.Escape(command);
        }

        public static async Task<string> process(string command, CancellationToken cancellationToken = default)
        {
            return (await ProcessStartAsync(command, cancellationToken)).StdOut;
        }

        public static async Task<(string StdOut, string StdError)> process2(string command, CancellationToken cancellationToken = default)
        {
            return await ProcessStartAsync(command, cancellationToken);
        }

        public static async Task<T> ignore<T>(Task<T> task)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (ProcessErrorException)
            {
                return default(T)!;
            }
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

        static async Task<(string StdOut, string StdError)> ProcessStartAsync(string command, CancellationToken cancellationToken, bool forceSilcent = false)
        {
            var cmd = shell + " " + command;
            var sbOut = new StringBuilder();
            var sbError = new StringBuilder();

            var (_, stdout, stderror) = ProcessX.GetDualAsyncEnumerable(cmd, workingDirectory, envVars);

            var runStdout = Task.Run(async () =>
            {
                var isFirst = true;
                await foreach (var item in stdout.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (!isFirst)
                    {
                        sbOut.AppendLine();
                    }
                    else
                    {
                        isFirst = false;
                    }

                    sbOut.Append(item);

                    if (verbose && !forceSilcent)
                    {
                        Console.WriteLine(item);
                    }
                }
            });

            var runStdError = Task.Run(async () =>
            {
                var isFirst = true;
                await foreach (var item in stderror.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (!isFirst)
                    {
                        sbOut.AppendLine();
                    }
                    else
                    {
                        isFirst = false;
                    }
                    sbError.Append(item);

                    if (verbose && !forceSilcent)
                    {
                        Console.WriteLine(item);
                    }
                }
            });

            await Task.WhenAll(runStdout, runStdError).ConfigureAwait(false);

            return (sbOut.ToString(), sbError.ToString());
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
