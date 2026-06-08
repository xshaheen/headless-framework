// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.AmbientTransactions;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Transactions;

internal sealed class MessageOutboxBuffer(IDispatcher dispatcher) : IAmbientWorkBuffer<MediumMessage>
{
    private readonly ConcurrentQueue<MediumMessage> _buffer = new();

    public void Buffer(MediumMessage work)
    {
        _buffer.Enqueue(work);
    }

    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        while (_buffer.TryDequeue(out var message))
        {
            if (message.Origin.Headers.ContainsKey(Headers.DelayTime))
            {
                await dispatcher
                    .EnqueueToScheduler(
                        message,
                        DateTime.Parse(message.Origin.Headers[Headers.SentTime]!, CultureInfo.InvariantCulture),
                        transaction: null,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                await dispatcher.EnqueueToPublish(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
