// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Framework.Messages.Messages;

namespace Framework.Messages.Internal;

public class MethodMatcherCache(IConsumerServiceSelector selector)
{
    private ConcurrentDictionary<string, IReadOnlyList<ConsumerExecutorDescriptor>> Entries { get; } = new();

    private ConcurrentDictionary<string, byte> GroupConcurrent { get; } = new();

    /// <summary>
    /// Get a dictionary of candidates.In the dictionary,
    /// the Key is the CAPSubscribeAttribute Group, the Value for the current Group of candidates
    /// </summary>
    public ConcurrentDictionary<
        string,
        IReadOnlyList<ConsumerExecutorDescriptor>
    > GetCandidatesMethodsOfGroupNameGrouped()
    {
        if (Entries.Count != 0)
        {
            return Entries;
        }

        var executorCollection = selector.SelectCandidates();

        foreach (var executor in executorCollection)
        {
            GroupConcurrent.AddOrUpdate(
                executor.Attribute.Group,
                executor.Attribute.GroupConcurrent,
                (group, val) => (byte)(val + executor.Attribute.GroupConcurrent)
            );
        }

        var groupedCandidates = executorCollection.GroupBy(x => x.Attribute.Group);

        foreach (var item in groupedCandidates)
        {
            Entries.TryAdd(item.Key, item.ToList());
        }

        return Entries;
    }

    public byte GetGroupConcurrentLimit(string group)
    {
        return GroupConcurrent.TryGetValue(group, out byte value) ? value : (byte)1;
    }

    public List<string> GetAllTopics()
    {
        if (Entries.Count == 0)
        {
            GetCandidatesMethodsOfGroupNameGrouped();
        }

        var result = new List<string>();
        foreach (var item in Entries.Values)
        {
            result.AddRange(item.Select(x => x.TopicName));
        }

        return result;
    }

    /// <summary>
    /// Attempts to get the topic executor associated with the specified topic name and group name from the
    /// <see cref="Entries" />.
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
        if (Entries == null)
        {
            throw new ArgumentNullException(nameof(Entries));
        }

        matchTopic = null;

        if (Entries.TryGetValue(groupName, out var groupMatchTopics))
        {
            matchTopic = selector.SelectBestCandidate(topicName, groupMatchTopics);

            return matchTopic != null;
        }

        return false;
    }
}
