// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.AzureServiceBus;

/// <summary>The entity names the Azure Service Bus transport derives from the host isolation prefix.</summary>
/// <remarks>
/// With a prefix (<c>Headless:Prefix</c>) every topic and Queue-lane queue starts with <c>{prefix}.</c>, so environments
/// that share one namespace never share either. A prefix holds no <c>.</c>, so it ends at the first dot and two different
/// prefixes never produce one name. Subscriptions are scoped by their topic and keep their names. An empty prefix leaves
/// every name exactly as it is without one.
/// </remarks>
internal static class AzureServiceBusPhysicalAddress
{
    // Service Bus limits topic and queue paths to 260 characters.
    private const int _MaxEntityPathLength = 260;

    /// <summary>Returns the topic path this host uses for <paramref name="topicPath"/>.</summary>
    /// <exception cref="InvalidOperationException">The prefix makes the path longer than Service Bus allows.</exception>
    public static string Topic(string hostPrefix, string topicPath) => _Prefixed(hostPrefix, topicPath, "topic");

    /// <summary>Returns the queue a Queue-lane message name maps to.</summary>
    /// <exception cref="InvalidOperationException">The prefix makes the queue path longer than Service Bus allows.</exception>
    public static string Queue(string hostPrefix, string messageName) => _Prefixed(hostPrefix, messageName, "queue");

    /// <summary>
    /// Returns the message name a Queue-lane queue was derived from, or <see langword="null"/> when the queue does not
    /// carry this host's prefix.
    /// </summary>
    public static string? QueueMessageName(string hostPrefix, string queuePath)
    {
        if (hostPrefix.Length == 0)
        {
            return queuePath;
        }

        var prefix = hostPrefix + ".";
        return queuePath.StartsWith(prefix, StringComparison.Ordinal) ? queuePath[prefix.Length..] : null;
    }

    private static string _Prefixed(string hostPrefix, string path, string kind)
    {
        if (hostPrefix.Length == 0)
        {
            return path;
        }

        var prefixed = $"{hostPrefix}.{path}";
        if (prefixed.Length > _MaxEntityPathLength)
        {
            throw new InvalidOperationException(
                $"The Azure Service Bus {kind} '{path}' with the host prefix '{hostPrefix}' (Headless:Prefix) is longer "
                    + $"than {_MaxEntityPathLength} characters. Shorten the {kind} name or the prefix."
            );
        }

        return prefixed;
    }
}
