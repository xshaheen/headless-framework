// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

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

    private ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> _entries = new(
        StringComparer.Ordinal
    );

    private ConcurrentDictionary<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>> _laneEntries =
        new();

    private ConcurrentDictionary<string, byte> _subscriptionConcurrent = new(StringComparer.Ordinal);

    private ConcurrentDictionary<ConsumerSubscriptionKey, byte> _laneSubscriptionConcurrent = new();

    private ConcurrentDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>> _identityEntries =
        new();

    private FrozenDictionary<InboxExecutorKey, ConsumerExecutorDescriptor> _inboxEntries = FrozenDictionary<
        InboxExecutorKey,
        ConsumerExecutorDescriptor
    >.Empty;

    /// <summary>
    /// Get a dictionary of candidates.In the dictionary,
    /// the Key is the subscription name, the Value for the current subscription of candidates
    /// </summary>
    public ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> GetCandidatesBySubscriptionName()
    {
        _EnsureEntries();
        return _entries;
    }

    internal ConcurrentDictionary<
        ConsumerSubscriptionKey,
        IReadOnlyList<ConsumerExecutorDescriptor>
    > GetCandidatesBySubscription()
    {
        _EnsureEntries();
        return _laneEntries;
    }

    private void _EnsureEntries()
    {
        if (!_entries.IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            if (!_entries.IsEmpty)
            {
                return;
            }

            var executorCollection = selector.SelectCandidates();

            var entries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
                StringComparer.Ordinal
            );
            var laneEntries =
                new ConcurrentDictionary<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            var groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var laneSubscriptionConcurrent = new ConcurrentDictionary<ConsumerSubscriptionKey, byte>();
            var subscriptionNameCandidates = executorCollection.GroupBy(
                x => x.SubscriptionName,
                StringComparer.Ordinal
            );

            foreach (var item in subscriptionNameCandidates)
            {
                var candidates = item.ToList();
                entries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                groupConcurrent.TryAdd(item.Key, maxConcurrency);
            }

            var laneSubscriptionCandidates = executorCollection.GroupBy(x => new ConsumerSubscriptionKey(
                x.SubscriptionName,
                x.Lane,
                x.SubscriptionKind
            ));

            foreach (var item in laneSubscriptionCandidates)
            {
                var candidates = item.ToList();
                laneEntries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                laneSubscriptionConcurrent.TryAdd(item.Key, maxConcurrency);
            }

            // Persisted non-inbox rows carry the consumer identity, not the subscription they arrived on, so a retry
            // finds its consumer by identity.
            var identityEntries =
                new ConcurrentDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            foreach (
                var item in executorCollection.GroupBy(x => new ConsumerIdentityKey(x.ResolvedConsumerIdentity, x.Lane))
            )
            {
                identityEntries.TryAdd(item.Key, item.ToList());
            }

            // Persisted inbox identities must not use subscription wildcards or their subscription-level caches, so an inbox
            // row finds its consumer by exact identity, contract version, and message name. Walking the lane entries in
            // their own order and keeping the first descriptor per key picks the one a scan of them would.
            var inboxEntries = new Dictionary<InboxExecutorKey, ConsumerExecutorDescriptor>();
            foreach (var entry in laneEntries)
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

            _entries = entries;
            _laneEntries = laneEntries;
            _identityEntries = identityEntries;
            _inboxEntries = inboxEntries.ToFrozenDictionary();
            _subscriptionConcurrent = groupConcurrent;
            _laneSubscriptionConcurrent = laneSubscriptionConcurrent;
        }
    }

    /// <summary>Gets the maximum consumer concurrency configured for the supplied subscription, or <c>1</c> when unknown.</summary>
    /// <param name="subscriptionName">The subscription name.</param>
    public byte GetSubscriptionConcurrentLimit(string subscriptionName)
    {
        _EnsureEntries();
        return _subscriptionConcurrent.TryGetValue(subscriptionName, out var value) ? value : (byte)1;
    }

    internal byte GetSubscriptionConcurrentLimit(ConsumerSubscriptionKey subscription)
    {
        _EnsureEntries();
        return _laneSubscriptionConcurrent.TryGetValue(subscription, out var value) ? value : (byte)1;
    }

    /// <summary>Gets the message names of every registered consumer across all subscriptions.</summary>
    public List<string> GetAllMessageNames()
    {
        if (_entries.IsEmpty)
        {
            GetCandidatesBySubscriptionName();
        }

        var result = new List<string>();
        foreach (var item in _entries.Values)
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

        _EnsureEntries();

        if (_entries.TryGetValue(subscriptionName, out var subscriptionCandidates))
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
        _EnsureEntries();

        if (_laneEntries.TryGetValue(subscription, out var subscriptionCandidates))
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
        _EnsureEntries();

        if (_identityEntries.TryGetValue(new ConsumerIdentityKey(consumerIdentity, lane), out var candidates))
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
        _EnsureEntries();

        return _inboxEntries.TryGetValue(
            new InboxExecutorKey(consumerIdentity, contractVersion, contractIdentity, lane),
            out descriptor
        );
    }

    /// <summary>Discards the cached topology so the next access rebuilds it from the consumer selector.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _entries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
                StringComparer.Ordinal
            );
            _laneEntries =
                new ConcurrentDictionary<ConsumerSubscriptionKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            _subscriptionConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            _laneSubscriptionConcurrent = new ConcurrentDictionary<ConsumerSubscriptionKey, byte>();
            _identityEntries =
                new ConcurrentDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            _inboxEntries = FrozenDictionary<InboxExecutorKey, ConsumerExecutorDescriptor>.Empty;
        }

        selector.Invalidate();
    }
}

/// <summary>
/// One subscription the host opens: a competing subscription and an every-instance subscription never share clients, even under one
/// name, because one is broker-durable and shared across processes and the other belongs to this process alone.
/// </summary>
internal readonly record struct ConsumerSubscriptionKey(
    string SubscriptionName,
    MessageLane Lane,
    Transport.ConsumerSubscriptionKind Kind = Transport.ConsumerSubscriptionKind.Competing
);

internal readonly record struct ConsumerIdentityKey(string ConsumerIdentity, MessageLane Lane);

/// <summary>The exact route an inbox row names; string parts compare ordinally, and a missing part matches only another.</summary>
internal readonly record struct InboxExecutorKey(
    string? ConsumerIdentity,
    string? ContractVersion,
    string MessageName,
    MessageLane Lane
);
