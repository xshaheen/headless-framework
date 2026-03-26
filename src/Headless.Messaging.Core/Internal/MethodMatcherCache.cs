// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

public class MethodMatcherCache(IConsumerServiceSelector selector)
{
    private readonly Lock _lock = new();
    private ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> _entries = new(
        StringComparer.Ordinal
    );

    private ConcurrentDictionary<string, byte> _groupConcurrent = new(StringComparer.Ordinal);

    /// <summary>
    /// Get a dictionary of candidates.In the dictionary,
    /// the Key is the Group name, the Value for the current Group of candidates
    /// </summary>
    public ConcurrentDictionary<
        string,
        IReadOnlyList<ConsumerExecutorDescriptor>
    > GetCandidatesMethodsOfGroupNameGrouped()
    {
        if (!_entries.IsEmpty)
        {
            return _entries;
        }

        lock (_lock)
        {
            if (!_entries.IsEmpty)
            {
                return _entries;
            }

            var executorCollection = selector.SelectCandidates();

            var entries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
                StringComparer.Ordinal
            );
            var groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var groupedCandidates = executorCollection.GroupBy(x => x.GroupName, StringComparer.Ordinal);

            foreach (var item in groupedCandidates)
            {
                var candidates = item.ToList();
                entries.TryAdd(item.Key, candidates);
                var maxConcurrency = candidates.Max(c => c.Concurrency);
                groupConcurrent.TryAdd(item.Key, maxConcurrency);
            }

            _entries = entries;
            _groupConcurrent = groupConcurrent;
        }

        return _entries;
    }

    public byte GetGroupConcurrentLimit(string group)
    {
        return _groupConcurrent.TryGetValue(group, out var value) ? value : (byte)1;
    }

    public List<string> GetAllTopics()
    {
        if (_entries.IsEmpty)
        {
            GetCandidatesMethodsOfGroupNameGrouped();
        }

        var result = new List<string>();
        foreach (var item in _entries.Values)
        {
            result.AddRange(item.Select(x => x.TopicName));
        }

        return result;
    }

    /// <summary>
    /// Attempts to get the topic executor associated with the specified topic name and group name from the
    /// cached descriptor snapshot.
    /// </summary>
    /// <param name="topicName">The topic name of the value to get.</param>
    /// <param name="groupName">The group name of the value to get.</param>
    /// <param name="matchTopic">topic executor of the value.</param>
    /// <returns>true if the key was found, otherwise false. </returns>
    public bool TryGetTopicExecutor(
        string topicName,
        string groupName,
        [NotNullWhen(true)] out ConsumerExecutorDescriptor? matchTopic
    )
    {
        matchTopic = null;

        if (_entries.IsEmpty)
        {
            GetCandidatesMethodsOfGroupNameGrouped();
        }

        if (_entries.TryGetValue(groupName, out var groupMatchTopics))
        {
            matchTopic = selector.SelectBestCandidate(topicName, groupMatchTopics);

            return matchTopic != null;
        }

        return false;
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _entries = new ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>>(
                StringComparer.Ordinal
            );
            _groupConcurrent = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        }

        selector.Invalidate();
    }
}
