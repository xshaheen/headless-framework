// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Messages;
using JetBrains.Annotations;

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

    private ConcurrentDictionary<ConsumerGroupKey, IReadOnlyList<ConsumerExecutorDescriptor>> _intentEntries = new();

    private ConcurrentDictionary<string, byte> _groupConcurrent = new(StringComparer.Ordinal);

    private ConcurrentDictionary<ConsumerGroupKey, byte> _intentGroupConcurrent = new();

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
    > GetCandidatesMethodsOfIntentGroupNameGrouped()
    {
        _EnsureEntries();
        return _intentEntries;
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
            var intentEntries = new ConcurrentDictionary<ConsumerGroupKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            var groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var intentGroupConcurrent = new ConcurrentDictionary<ConsumerGroupKey, byte>();
            var groupedCandidates = executorCollection.GroupBy(x => x.GroupName, StringComparer.Ordinal);

            foreach (var item in groupedCandidates)
            {
                var candidates = item.ToList();
                entries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                groupConcurrent.TryAdd(item.Key, maxConcurrency);
            }

            var intentGroupedCandidates = executorCollection.GroupBy(x => new ConsumerGroupKey(
                x.GroupName,
                x.IntentType
            ));

            foreach (var item in intentGroupedCandidates)
            {
                var candidates = item.ToList();
                intentEntries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                intentGroupConcurrent.TryAdd(item.Key, maxConcurrency);
            }

            _entries = entries;
            _intentEntries = intentEntries;
            _groupConcurrent = groupConcurrent;
            _intentGroupConcurrent = intentGroupConcurrent;
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
        return _intentGroupConcurrent.TryGetValue(group, out var value) ? value : (byte)1;
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
        IntentType intentType,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? matchMessageName
    )
    {
        matchMessageName = null;
        _EnsureEntries();

        if (_intentEntries.TryGetValue(new ConsumerGroupKey(groupName, intentType), out var groupMatchMessageNames))
        {
            matchMessageName = selector.SelectBestCandidate(messageName, groupMatchMessageNames);
            return matchMessageName is not null;
        }

        return false;
    }

    /// <summary>Discards the cached topology so the next access rebuilds it from the consumer selector.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _entries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
                StringComparer.Ordinal
            );
            _intentEntries = new ConcurrentDictionary<ConsumerGroupKey, IReadOnlyList<ConsumerExecutorDescriptor>>();
            _groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            _intentGroupConcurrent = new ConcurrentDictionary<ConsumerGroupKey, byte>();
        }

        selector.Invalidate();
    }
}

internal readonly record struct ConsumerGroupKey(string GroupName, IntentType IntentType);
