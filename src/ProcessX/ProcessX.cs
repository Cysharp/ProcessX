using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cysharp.Diagnostics
{
    public static class ProcessX
    {
        public static ProcessAsyncEnumerable StartAsync(string command, string workingDirectory = null, IDictionary<string, string> environmentVariable = null, Encoding encoding = null)
        {
            var cmdBegin = command.IndexOf(' ');
            if (cmdBegin == -1)
            {
                return StartAsync(command, null, workingDirectory, environmentVariable, encoding);
            }
            else
            {
                var fileName = command.Substring(0, cmdBegin);
                var arguments = command.Substring(cmdBegin + 1, command.Length - (cmdBegin + 1));
                return StartAsync(fileName, arguments, workingDirectory, environmentVariable, encoding);
            }
        }

        public static ProcessAsyncEnumerable StartAsync(string fileName, string arguments, string workingDirectory = null, IDictionary<string, string> environmentVariable = null, Encoding encoding = null)
        {
            var pi = new ProcessStartInfo()
            {
                FileName = fileName,
                Arguments = arguments,
            };

            if (workingDirectory != null)
            {
                pi.WorkingDirectory = workingDirectory;
            }

            if (environmentVariable != null)
            {
                foreach (var item in environmentVariable)
                {
                    pi.EnvironmentVariables.Add(item.Key, item.Value);
                }
            }

            if (encoding != null)
            {
                pi.StandardOutputEncoding = encoding;
                pi.StandardErrorEncoding = encoding;
            }

            return StartAsync(pi);
        }

        public static ProcessAsyncEnumerable StartAsync(ProcessStartInfo processStartInfo)
        {
            // override setings.
            processStartInfo.UseShellExecute = false;
            processStartInfo.CreateNoWindow = true;
            processStartInfo.ErrorDialog = false;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;

            var process = new Process()
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true,
            };

            var outputChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

            var errorList = new List<string>();

            var waitOutputDataCompleted = new TaskCompletionSource<object>();
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputChannel.Writer.TryWrite(e.Data);
                }
                else
                {
                    waitOutputDataCompleted.TrySetResult(null);
                }
            };

            var waitErrorDataCompleted = new TaskCompletionSource<object>();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (errorList)
                    {
                        errorList.Add(e.Data);
                    }
                }
                else
                {
                    waitErrorDataCompleted.TrySetResult(null);
                }
            };

            process.Exited += async (sender, e) =>
            {
                await waitOutputDataCompleted.Task.ConfigureAwait(false);
                await waitErrorDataCompleted.Task.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    outputChannel.Writer.TryComplete(new ProcessErrorException(process.ExitCode, errorList.ToArray()));
                }
                else
                {
                    if (errorList.Count == 0)
                    {
                        outputChannel.Writer.TryComplete();
                    }
                    else
                    {
                        outputChannel.Writer.TryComplete(new ProcessErrorException(process.ExitCode, errorList.ToArray()));
                    }
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Can't start process. FileName:" + processStartInfo.FileName + ", Arguments:" + processStartInfo.Arguments);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return new ProcessAsyncEnumerable(process, outputChannel.Reader);
        }
    }
}