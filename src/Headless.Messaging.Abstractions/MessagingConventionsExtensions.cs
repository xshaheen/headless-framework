// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Extension methods for configuring <see cref="MessagingConventions"/>.
/// </summary>
public static class MessagingConventionsExtensions
{
    /// <summary>
    /// Configures the messaging conventions to use kebab-case for topic names.
    /// Example: "OrderCreated" becomes "order-created".
    /// </summary>
    public static MessagingConventions UseKebabCaseTopics(this MessagingConventions conventions)
    {
        conventions.TopicNaming = TopicNamingConvention.KebabCase;
        return conventions;
    }

    /// <summary>
    /// Sets a prefix to prepend to all generated topic names.
    /// </summary>
    /// <param name="conventions">The conventions to configure.</param>
    /// <param name="prefix">The prefix to prepend (e.g., "prod.").</param>
    public static MessagingConventions WithTopicPrefix(this MessagingConventions conventions, string prefix)
    {
        conventions.TopicPrefix = prefix;
        return conventions;
    }

    /// <summary>
    /// Sets a suffix to append to all generated topic names.
    /// </summary>
    /// <param name="conventions">The conventions to configure.</param>
    /// <param name="suffix">The suffix to append (e.g., ".v1").</param>
    public static MessagingConventions WithTopicSuffix(this MessagingConventions conventions, string suffix)
    {
        conventions.TopicSuffix = suffix;
        return conventions;
    }

    /// <summary>
    /// Sets the default consumer group name for consumers that don't explicitly specify a group.
    /// </summary>
    /// <param name="conventions">The conventions to configure.</param>
    /// <param name="group">The default group name.</param>
    public static MessagingConventions WithDefaultGroup(this MessagingConventions conventions, string group)
    {
        conventions.DefaultGroup = group;
        return conventions;
    }
}
