// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Runtime;

/// <summary>
/// Caches the resolved consumer topology (message name plus group to executor descriptor) so the
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

    private ConcurrentDictionary<ConsumerGroupKey, IReadOnlyList<ConsumerExecutorDescriptor>> _laneEntries = new();

    private ConcurrentDictionary<string, byte> _groupConcurrent = new(StringComparer.Ordinal);

    private ConcurrentDictionary<ConsumerGroupKey, byte> _laneGroupConcurrent = new();

    private ConcurrentDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>> _identityEntries =
        new();

    private FrozenDictionary<InboxExecutorKey, ConsumerExecutorDescriptor> _inboxEntries = FrozenDictionary<
        InboxExecutorKey,
        ConsumerExecutorDescriptor
    >.Empty;

    /// <summary>
    /// Get a dictionary of candidates.In the dictionary,
    /// the Key is the Group name, the Value for the current Group of candidates
    /// </summary>
    public ConcurrentDictionary<
        string,
        IReadOnlyList<ConsumerExecutorDescriptor>
    > GetCandidatesMethodsOfGroupNameGrouped()
    {
        _EnsureEntries();
        return _entries;
    }

    internal ConcurrentDictionary<
        ConsumerGroupKey,
        IReadOnlyList<ConsumerExecutorDescriptor>
    > GetCandidatesMethodsOfLaneGroupNameGrouped()
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
            var laneEntries = new ConcurrentDictionary<ConsumerGroupKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            var groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var laneGroupConcurrent = new ConcurrentDictionary<ConsumerGroupKey, byte>();
            var groupedCandidates = executorCollection.GroupBy(x => x.GroupName, StringComparer.Ordinal);

            foreach (var item in groupedCandidates)
            {
                var candidates = item.ToList();
                entries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                groupConcurrent.TryAdd(item.Key, maxConcurrency);
            }

            var laneGroupedCandidates = executorCollection.GroupBy(x => new ConsumerGroupKey(x.GroupName, x.Lane));

            foreach (var item in laneGroupedCandidates)
            {
                var candidates = item.ToList();
                laneEntries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                laneGroupConcurrent.TryAdd(item.Key, maxConcurrency);
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

            // Persisted inbox identities must not use subscription wildcards or their group-level caches, so an inbox
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
            _groupConcurrent = groupConcurrent;
            _laneGroupConcurrent = laneGroupConcurrent;
        }
    }

    /// <summary>Gets the maximum consumer concurrency configured for the supplied group, or <c>1</c> when unknown.</summary>
    /// <param name="group">The consumer group name.</param>
    public byte GetGroupConcurrentLimit(string group)
    {
        _EnsureEntries();
        return _groupConcurrent.TryGetValue(group, out var value) ? value : (byte)1;
    }

    internal byte GetGroupConcurrentLimit(ConsumerGroupKey group)
    {
        _EnsureEntries();
        return _laneGroupConcurrent.TryGetValue(group, out var value) ? value : (byte)1;
    }

    /// <summary>Gets the message names of every registered consumer across all groups.</summary>
    public List<string> GetAllMessageNames()
    {
        if (_entries.IsEmpty)
        {
            GetCandidatesMethodsOfGroupNameGrouped();
        }

        var result = new List<string>();
        foreach (var item in _entries.Values)
        {
            result.AddRange(item.Select(x => x.MessageName));
        }

        return result;
    }

    /// <summary>
    /// Attempts to get the message executor associated with the specified message name and group name from the
    /// cached descriptor snapshot.
    /// </summary>
    /// <param name="messageName">The message name of the value to get.</param>
    /// <param name="groupName">The group name of the value to get.</param>
    /// <param name="matchMessageName">message name executor of the value.</param>
    /// <returns>true if the key was found, otherwise false. </returns>
    public bool TryGetMessageNameExecutor(
        string messageName,
        string groupName,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? matchMessageName
    )
    {
        matchMessageName = null;

        _EnsureEntries();

        if (_entries.TryGetValue(groupName, out var groupMatchMessageNames))
        {
            matchMessageName = selector.SelectBestCandidate(messageName, groupMatchMessageNames);

            return matchMessageName != null;
        }

        return false;
    }

    internal bool TryGetMessageNameExecutor(
        string messageName,
        string groupName,
        MessageLane lane,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? matchMessageName
    )
    {
        matchMessageName = null;
        _EnsureEntries();

        if (_laneEntries.TryGetValue(new ConsumerGroupKey(groupName, lane), out var groupMatchMessageNames))
        {
            matchMessageName = selector.SelectBestCandidate(messageName, groupMatchMessageNames);
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
            _laneEntries = new ConcurrentDictionary<ConsumerGroupKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            _groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            _laneGroupConcurrent = new ConcurrentDictionary<ConsumerGroupKey, byte>();
            _identityEntries =
                new ConcurrentDictionary<ConsumerIdentityKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            _inboxEntries = FrozenDictionary<InboxExecutorKey, ConsumerExecutorDescriptor>.Empty;
        }

        selector.Invalidate();
    }
}

internal readonly record struct ConsumerGroupKey(string GroupName, MessageLane Lane);

internal readonly record struct ConsumerIdentityKey(string ConsumerIdentity, MessageLane Lane);

/// <summary>The exact route an inbox row names; string parts compare ordinally, and a missing part matches only another.</summary>
internal readonly record struct InboxExecutorKey(
    string? ConsumerIdentity,
    string? ContractVersion,
    string MessageName,
    MessageLane Lane
);
