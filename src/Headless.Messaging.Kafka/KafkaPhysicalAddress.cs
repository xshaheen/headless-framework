// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Kafka;

/// <summary>The names the Kafka transport derives: topics, topic patterns, and consumer groups.</summary>
/// <remarks>
/// With a host isolation prefix (<c>Headless:Prefix</c>) every topic and consumer group starts with <c>{prefix}.</c>, so
/// environments that share one cluster never share either. A prefix holds no <c>.</c>, so it ends at the first dot and
/// two different prefixes never produce one name. Without a prefix a topic is its message name verbatim, as before.
/// </remarks>
internal static class KafkaPhysicalAddress
{
    // Kafka does not restrict group.id, so the group takes the topic naming rules (up to 249 of [a-zA-Z0-9._-]): a Queue
    // message name, which is already a legal topic, becomes its consumer group unchanged on every host.
    private static readonly BusNameRules _TopicRules = new(
        maxLength: 249,
        isAllowed: static c => c is '.' or '_' or '-'
    );

    /// <summary>
    /// Returns the topic a message name maps to. A prefixed topic past Kafka's 249-character limit keeps a readable
    /// prefix and ends with a hash of the whole name, which every host derives the same way.
    /// </summary>
    public static string Topic(string hostPrefix, string messageName) =>
        hostPrefix.Length == 0 ? messageName : BusNameBuilder.Build($"{hostPrefix}.{messageName}", _TopicRules);

    /// <summary>
    /// Returns the subscription pattern for a wildcard message name, matching only topics of this host's prefix.
    /// </summary>
    /// <param name="hostPrefix">The host isolation prefix, or empty for none.</param>
    /// <param name="pattern">The anchored pattern <see cref="TransportNaming.WildcardToRegex"/> built.</param>
    public static string TopicPattern(string hostPrefix, string pattern)
    {
        if (hostPrefix.Length == 0)
        {
            return pattern;
        }

        // librdkafka treats a subscription that starts with '^' as a pattern, so the prefix goes after the anchor.
        var unanchored = pattern.StartsWith('^', StringComparison.Ordinal) ? pattern[1..] : pattern;
        return "^" + Regex.Escape(hostPrefix + ".") + unanchored;
    }

    /// <summary>Returns the consumer group of a Queue subscription, which is named after its message.</summary>
    public static string GroupId(string hostPrefix, string subscriptionName) =>
        BusNameBuilder.Build(
            hostPrefix.Length == 0 ? subscriptionName : $"{hostPrefix}.{subscriptionName}",
            _TopicRules
        );
}
