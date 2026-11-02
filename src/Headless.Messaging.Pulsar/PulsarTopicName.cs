// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Pulsar;

/// <summary>
/// Reads Pulsar topic names in every form the broker and the client report them: a short name (<c>orders</c>), a
/// namespace-relative name (<c>tenant/namespace/orders</c>), or a complete name
/// (<c>persistent://tenant/namespace/orders</c>), each optionally with a <c>-partition-N</c> suffix.
/// </summary>
internal static class PulsarTopicName
{
    private const string _DefaultDomain = "persistent://";
    private const string _DefaultNamespace = "public/default/";
    private const string _PartitionSuffix = "-partition-";

    /// <summary>Returns the complete name of <paramref name="topic"/>, keeping any partition suffix.</summary>
    public static string Complete(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        if (topic.Contains("://", StringComparison.Ordinal))
        {
            return topic;
        }

        return topic.Contains('/', StringComparison.Ordinal)
            ? _DefaultDomain + topic
            : _DefaultDomain + _DefaultNamespace + topic;
    }

    /// <summary>Returns the complete name of <paramref name="topic"/> without its partition suffix.</summary>
    public static string Normalize(string topic)
    {
        var complete = Complete(topic);
        var suffixStart = _PartitionSuffixStart(complete);

        return suffixStart < 0 ? complete : complete[..suffixStart];
    }

    /// <summary>Returns the name of partition <paramref name="partition"/> of <paramref name="topic"/>.</summary>
    public static string Partition(string topic, int partition) =>
        string.Create(CultureInfo.InvariantCulture, $"{topic}{_PartitionSuffix}{partition}");

    // The suffix counts only when the last path segment ends with "-partition-" and a non-negative number, as Pulsar's own
    // TopicName parses it.
    private static int _PartitionSuffixStart(string topic)
    {
        var localStart = topic.LastIndexOf('/') + 1;
        var suffixStart = topic.LastIndexOf(_PartitionSuffix, StringComparison.Ordinal);

        if (suffixStart < localStart)
        {
            return -1;
        }

        var digits = topic.AsSpan(suffixStart + _PartitionSuffix.Length);

        return digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? suffixStart
            : -1;
    }
}
