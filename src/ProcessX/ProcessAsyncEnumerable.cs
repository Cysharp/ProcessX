using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cysharp.Diagnostics
{
    public class ProcessAsyncEnumerable : IAsyncEnumerable<string>
    {
        readonly Process process;
        readonly ChannelReader<string> channel;

        public ProcessAsyncEnumerable(Process process, ChannelReader<string> channel)
        {
            this.process = process;
            this.channel = channel;
        }

        public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new ProcessAsyncEnumerator(process, channel, cancellationToken);
        }

        public async Task<string[]> AsTask(CancellationToken cancellationToken = default)
        {
            var list = new List<string>();
            await foreach (var item in this.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                list.Add(item);
            }
            return list.ToArray();
        }
    }
}