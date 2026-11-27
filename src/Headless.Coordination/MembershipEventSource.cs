// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

/// <summary>Fans membership events out to subscribers and keeps the bounded local event log.</summary>
/// <param name="capacity">The buffer of each <see cref="WatchAsync"/> subscriber; the event log keeps its own size.</param>
internal sealed class MembershipEventSource(
    ILogger<MembershipEventSource> logger,
    TimeProvider timeProvider,
    int capacity = 256
) : IMembershipEventSource, IMembershipEventLog
{
    private const int _LogCapacity = 256;

    private readonly Lock _gate = new();
    private readonly MembershipEventLogEntry[] _log = new MembershipEventLogEntry[_LogCapacity];
    private long _sequence;
    private ImmutableArray<Channel<NodeMembershipEvent>> _subscribers = [];

    public IReadOnlyList<MembershipEventLogEntry> GetEntries()
    {
        lock (_gate)
        {
            var count = (int)Math.Min(_sequence, _LogCapacity);
            var entries = new MembershipEventLogEntry[count];
            var oldest = _sequence - count + 1;

            for (var i = 0; i < count; i++)
            {
                entries[i] = _log[(int)((oldest + i - 1) % _LogCapacity)];
            }

            return entries;
        }
    }

    /// <summary>Streams best-effort membership observations until <paramref name="cancellationToken"/> is cancelled.</summary>
    /// <remarks>
    /// Consumers MUST dispose the returned async enumerator (or cancel <paramref name="cancellationToken"/>) to
    /// release the underlying subscription. Abandoning the enumerator without disposal leaks its channel until the
    /// next publish attempt observes the completed channel and prunes it.
    /// </remarks>
    public IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<NodeMembershipEvent>(
            new BoundedChannelOptions(capacity)
            {
                // Wait + TryWrite is the non-blocking drop-and-log pairing: TryWrite returns false when the
                // bounded channel is full (a DropWrite/DropOldest mode would instead return true and drop
                // silently, losing the lagging-subscriber visibility logged below).
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

    /// <summary>Records <paramref name="event"/> in the log, then offers it to every subscriber.</summary>
    /// <param name="event">The membership event.</param>
    /// <param name="snapshot">The liveness snapshot the event came from, or <see langword="null"/> when none did.</param>
    /// <param name="isFirstObservation">Whether the event reports the first sighting of an incarnation.</param>
    internal void Publish(
        NodeMembershipEvent @event,
        NodeLivenessSnapshot? snapshot = null,
        bool isFirstObservation = false
    )
    {
        ImmutableArray<Channel<NodeMembershipEvent>> subscribers;

        // The log is written whether or not anyone watches: both the heartbeat and the membership-loss path publish,
        // and a reader that polls later must still see what happened.
        lock (_gate)
        {
            var sequence = ++_sequence;
            _log[(int)((sequence - 1) % _LogCapacity)] = new MembershipEventLogEntry
            {
                Sequence = sequence,
                Event = @event,
                ObservedAt = timeProvider.GetUtcNow(),
                State = snapshot?.State,
                LastHeartbeatAt = snapshot?.LastHeartbeatAt,
                IsFirstObservation = isFirstObservation,
            };
            subscribers = _subscribers;
        }

        List<Channel<NodeMembershipEvent>>? dead = null;

        foreach (var subscriber in subscribers)
        {
            if (subscriber.Writer.TryWrite(@event))
            {
                continue;
            }

            // TryWrite returns false for two distinct reasons under FullMode.Wait: the channel is full
            // (legitimate lagging-subscriber drop — keep it and log) or the channel is completed because the
            // reader's enumerator was disposed/cancelled (subscriber gone — prune it instead of log-spamming).
            if (subscriber.Reader.Completion.IsCompleted)
            {
                (dead ??= []).Add(subscriber);
            }
            else
            {
                logger.MembershipEventDropped(_Discriminator(@event), @event.Identity);
            }
        }

        if (dead is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var channel in dead)
            {
                _subscribers = _subscribers.Remove(channel);
            }
        }
    }

    private static string _Discriminator(NodeMembershipEvent @event)
    {
        // Avoid reflection (GetType().Name) on the drop path.
        return @event switch
        {
            NodeJoined => nameof(NodeJoined),
            NodeSuspected => nameof(NodeSuspected),
            NodeRecovered => nameof(NodeRecovered),
            NodeLeft => nameof(NodeLeft),
            LocalMembershipLost => nameof(LocalMembershipLost),
            _ => @event.GetType().Name,
        };
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
