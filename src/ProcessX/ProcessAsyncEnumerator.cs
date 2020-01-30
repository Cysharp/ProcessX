using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cysharp.Diagnostics
{
    class ProcessAsyncEnumerator : IAsyncEnumerator<string>
    {
        readonly Process? process;
        readonly ChannelReader<string> channel;
        readonly CancellationToken cancellationToken;
        readonly CancellationTokenRegistration cancellationTokenRegistration;
        string? current;
        bool disposed;

        public ProcessAsyncEnumerator(Process? process, ChannelReader<string> channel, CancellationToken cancellationToken)
        {
            // process is not null, kill when canceled.
            this.process = process;
            this.channel = channel;
            this.cancellationToken = cancellationToken;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationTokenRegistration = cancellationToken.Register(() =>
                {
                    _ = DisposeAsync();
                });
            }
        }

#pragma warning disable CS8603
        // when call after MoveNext, current always not null.
        public string Current => current;
#pragma warning restore CS8603

        public async ValueTask<bool> MoveNextAsync()
        {
            if (channel.TryRead(out current))
            {
                return true;
            }
            else
            {
                if (await channel.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (channel.TryRead(out current))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                try
                {
                    cancellationTokenRegistration.Dispose();
                    if (process != null)
                    {
                        process.EnableRaisingEvents = false;
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                }
                finally
                {
                    if (process != null)
                    {
                        process.Dispose();
                    }
                }
            }

            return default;
        }
    }
}
