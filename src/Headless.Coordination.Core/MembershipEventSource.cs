// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

[PublicAPI]
public sealed class MembershipEventSource(ILogger<MembershipEventSource> logger, int capacity = 256)
    : IMembershipEventSource
{
    private readonly Lock _gate = new();
    private ImmutableArray<Channel<NodeMembershipEvent>> _subscribers = [];

    public IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<NodeMembershipEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        lock (_gate)
        {
            _subscribers = _subscribers.Add(channel);
        }

        return _ReadAsync(channel, cancellationToken);
    }

    internal void Publish(NodeMembershipEvent @event)
    {
        ImmutableArray<Channel<NodeMembershipEvent>> subscribers;

        lock (_gate)
        {
            subscribers = _subscribers;
        }

        foreach (var subscriber in subscribers)
        {
            if (!subscriber.Writer.TryWrite(@event))
            {
                logger.MembershipEventDropped(@event.GetType().Name, @event.Identity);
            }
        }
    }

    private async IAsyncEnumerable<NodeMembershipEvent> _ReadAsync(
        Channel<NodeMembershipEvent> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        try
        {
            await foreach (var @event in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return @event;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers = _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }
}
