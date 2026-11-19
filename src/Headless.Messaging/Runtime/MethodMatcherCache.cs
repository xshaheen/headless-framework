// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Caches the resolved consumer topology (message name plus subscription to executor descriptor) so the
/// dispatch hot path and dashboards can look up handlers without re-running consumer selection. The
/// snapshot is lazily built on first access and rebuilt after <see cref="Invalidate"/>.
/// </summary>
[PublicAPI]
public class MethodMatcherCache(IConsumerServiceSelector selector)
{
    private readonly Lock _lock = new();

    // Every lookup reads one immutable snapshot, published and dropped with a single reference write, so a reader that
    // overlaps a rebuild or an invalidation sees the whole old topology or the whole new one, never a mix of the two
    // or an empty map that looks valid. Null means the next lookup must rebuild.
    private Snapshot? _snapshot;

    /// <summary>
    /// Get a dictionary of candidates.In the dictionary,
    /// the Key is the subscription name, the Value for the current subscription of candidates
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> GetCandidatesBySubscriptionName()
    {
        return _GetSnapshot().Entries;
    }

    internal IReadOnlyDictionary<
        ConsumerSubscriptionKey,
        IReadOnlyList<ConsumerExecutorDescriptor>
    > GetCandidatesBySubscription()
    {
        return _GetSnapshot().LaneEntries;
    }

    private Snapshot _GetSnapshot()
    {
        return Volatile.Read(ref _snapshot) ?? _BuildSnapshot();
    }

    private Snapshot _BuildSnapshot()
    {
        lock (_lock)
        {
            if (_snapshot is { } current)
            {
                return current;
            }

            // Selecting and publishing under the lock keeps a fill that started before Invalidate from landing after
            // it: Invalidate takes the same lock, so it drops whatever this publishes.
            var snapshot = Snapshot.Create(selector.SelectCandidates());
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    /// <summary>Gets the maximum consumer concurrency configured for the supplied subscription, or <c>1</c> when unknown.</summary>
    /// <param name="subscriptionName">The subscription name.</param>
    public byte GetSubscriptionConcurrentLimit(string subscriptionName)
    {
        return _GetSnapshot().SubscriptionConcurrent.TryGetValue(subscriptionName, out var value) ? value : (byte)1;
    }

    internal byte GetSubscriptionConcurrentLimit(ConsumerSubscriptionKey subscription)
    {
        return _GetSnapshot().LaneSubscriptionConcurrent.TryGetValue(subscription, out var value) ? value : (byte)1;
    }

    /// <summary>Gets the message names of every registered consumer across all subscriptions.</summary>
    public List<string> GetAllMessageNames()
    {
        var result = new List<string>();
        foreach (var item in _GetSnapshot().Entries.Values)
        {
            result.AddRange(item.Select(x => x.MessageName));
        }

        return result;
    }

    /// <summary>
    /// Attempts to get the message executor associated with the specified message name and subscription name from the
    /// cached descriptor snapshot.
    /// </summary>
    /// <param name="messageName">The message name of the value to get.</param>
    /// <param name="subscriptionName">The subscription name of the value to get.</param>
    /// <param name="matchMessageName">message name executor of the value.</param>
    /// <returns>true if the key was found, otherwise false. </returns>
    public bool TryGetMessageNameExecutor(
        string messageName,
        string subscriptionName,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? matchMessageName
    )
    {
        matchMessageName = null;

        if (_GetSnapshot().Entries.TryGetValue(subscriptionName, out var subscriptionCandidates))
        {
            matchMessageName = selector.SelectBestCandidate(messageName, subscriptionCandidates);

            return matchMessageName != null;
        }

        return false;
    }

    internal bool TryGetMessageNameExecutor(
        string messageName,
        ConsumerSubscriptionKey subscription,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? matchMessageName
    )
    {
        matchMessageName = null;

        if (_GetSnapshot().LaneEntries.TryGetValue(subscription, out var subscriptionCandidates))
        {
            matchMessageName = selector.SelectBestCandidate(messageName, subscriptionCandidates);
            return matchMessageName is not null;
        }

        return false;
    }

    internal bool TryGetConsumerIdentityExecutor(
        string messageName,
        string consumerIdentity,
        MessageLane lane,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? descriptor
    )
    {
        descriptor = null;

        if (
            _GetSnapshot()
                .IdentityEntries.TryGetValue(new ConsumerIdentityKey(consumerIdentity, lane), out var candidates)
        )
        {
            descriptor = selector.SelectBestCandidate(messageName, candidates);
            return descriptor is not null;
        }

        return false;
    }

    internal bool TryGetInboxExecutor(
        string consumerIdentity,
        string contractIdentity,
        string contractVersion,
        MessageLane lane,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? descriptor
    )
    {
        return _GetSnapshot()
            .InboxEntries.TryGetValue(
                new InboxExecutorKey(consumerIdentity, contractVersion, contractIdentity, lane),
                out descriptor
            );
    }

    /// <summary>
    /// Finds the consumer of a stored received row: by its inbox identity when it has one, else by the consumer
    /// identity its envelope carries.
    /// </summary>
    internal bool TryGetStoredMessageExecutor(
        MediumMessage message,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? descriptor
    )
    {
        if (message.InboxKey is { } inboxKey)
        {
            return TryGetInboxExecutor(
                inboxKey.ConsumerIdentity,
                inboxKey.ContractIdentity,
                inboxKey.ContractVersion,
                inboxKey.Lane,
                out descriptor
            );
        }

        descriptor = null;

        return message.Origin.GetConsumerIdentity() is { } consumerIdentity
            && TryGetConsumerIdentityExecutor(message.Origin.Name, consumerIdentity, message.Lane, out descriptor);
    }

    /// <summary>Discards the cached topology so the next access rebuilds it from the consumer selector.</summary>
    public void Invalidate()
    {
        selector.Invalidate();

        lock (_lock)
        {
            Volatile.Write(ref _snapshot, null);
        }
    }

    private sealed class Snapshot
    {
        private Snapshot(
            FrozenDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> entries,
            FrozenDictionary<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>> laneEntries,
            FrozenDictionary<string, byte> subscriptionConcurrent,
            FrozenDictionary<ConsumerSubscriptionKey, byte> laneSubscriptionConcurrent,
            FrozenDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>> identityEntries,
            FrozenDictionary<InboxExecutorKey, ConsumerExecutorDescriptor> inboxEntries
        )
        {
            Entries = entries;
            LaneEntries = laneEntries;
            SubscriptionConcurrent = subscriptionConcurrent;
            LaneSubscriptionConcurrent = laneSubscriptionConcurrent;
            IdentityEntries = identityEntries;
            InboxEntries = inboxEntries;
        }

        public FrozenDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> Entries { get; }

        public FrozenDictionary<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>> LaneEntries { get; }

        public FrozenDictionary<string, byte> SubscriptionConcurrent { get; }

        public FrozenDictionary<ConsumerSubscriptionKey, byte> LaneSubscriptionConcurrent { get; }

        public FrozenDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>> IdentityEntries { get; }

        public FrozenDictionary<InboxExecutorKey, ConsumerExecutorDescriptor> InboxEntries { get; }

        public static Snapshot Create(IReadOnlyList<ConsumerExecutorDescriptor> executorCollection)
        {
            var entries = new Dictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(StringComparer.Ordinal);
            var subscriptionConcurrent = new Dictionary<string, byte>(StringComparer.Ordinal);

            foreach (var item in executorCollection.GroupBy(x => x.SubscriptionName, StringComparer.Ordinal))
            {
                var candidates = item.ToList();
                entries.Add(item.Key, candidates);
                subscriptionConcurrent.Add(item.Key, candidates.Max(c => c.Concurrency));
            }

            var laneEntries = new Dictionary<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            var laneSubscriptionConcurrent = new Dictionary<ConsumerSubscriptionKey, byte>();

            foreach (
                var item in executorCollection.GroupBy(x => new ConsumerSubscriptionKey(
                    x.SubscriptionName,
                    x.Lane,
                    x.SubscriptionKind,
                    x.TransportName
                ))
            )
            {
                var candidates = item.ToList();
                laneEntries.Add(item.Key, candidates);
                laneSubscriptionConcurrent.Add(item.Key, candidates.Max(c => c.Concurrency));
            }

            var frozenLaneEntries = laneEntries.ToFrozenDictionary();

            // Persisted non-inbox rows carry the consumer identity, not the subscription they arrived on, so a retry
            // finds its consumer by identity.
            var identityEntries = executorCollection
                .GroupBy(x => new ConsumerIdentityKey(x.ResolvedConsumerIdentity, x.Lane))
                .ToFrozenDictionary(item => item.Key, IReadOnlyList<ConsumerExecutorDescriptor> (item) => [.. item]);

            // Persisted inbox identities must not use subscription wildcards or their subscription-level caches, so an
            // inbox row finds its consumer by exact identity, contract version, and message name. Walking the lane
            // entries in their own order and keeping the first descriptor per key picks the one a scan of them would.
            var inboxEntries = new Dictionary<InboxExecutorKey, ConsumerExecutorDescriptor>();
            foreach (var entry in frozenLaneEntries)
            {
                foreach (var candidate in entry.Value)
                {
                    inboxEntries.TryAdd(
                        new InboxExecutorKey(
                            candidate.ConsumerIdentity,
                            candidate.MessageContractVersion,
                            candidate.MessageName,
                            entry.Key.Lane
                        ),
                        candidate
                    );
                }
            }

            return new Snapshot(
                entries.ToFrozenDictionary(StringComparer.Ordinal),
                frozenLaneEntries,
                subscriptionConcurrent.ToFrozenDictionary(StringComparer.Ordinal),
                laneSubscriptionConcurrent.ToFrozenDictionary(),
                identityEntries,
                inboxEntries.ToFrozenDictionary()
            );
        }
    }
}

/// <summary>
/// One subscription the host opens: a competing subscription and an every-instance subscription never share clients, even under one
/// name, because one is broker-durable and shared across processes and the other belongs to this process alone.
/// </summary>
internal readonly record struct ConsumerSubscriptionKey(
    string SubscriptionName,
    MessageLane Lane,
    Transport.ConsumerSubscriptionKind Kind = Transport.ConsumerSubscriptionKind.Competing,
    // The named transport the subscription opens on, null for the default one. Part of the key so a subscription never
    // mixes consumers bound to different brokers.
    string? TransportName = null
);

internal readonly record struct ConsumerIdentityKey(string ConsumerIdentity, MessageLane Lane);

/// <summary>The exact route an inbox row names; string parts compare ordinally, and a missing part matches only another.</summary>
internal readonly record struct InboxExecutorKey(
    string? ConsumerIdentity,
    string? ContractVersion,
    string MessageName,
    MessageLane Lane
);
