// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// Provides discovery and introspection capabilities for registered message consumers.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables agent-native workflows by providing programmatic access
/// to consumer registration metadata. Use cases include runtime validation,
/// documentation generation, testing, and debugging of message routing tables.
/// </para>
/// <para>
/// The registry is frozen after first read, ensuring thread-safe zero-allocation access
/// to consumer metadata at runtime.
/// </para>
/// </remarks>
public interface IConsumerRegistry
{
    /// <summary>
    /// Gets all registered consumer metadata.
    /// </summary>
    /// <returns>A read-only list of all registered consumer metadata.</returns>
    /// <remarks>
    /// Calling this method freezes the registry, preventing further registrations.
    /// Subsequent calls return the same cached read-only list for zero-allocation access.
    /// </remarks>
    IReadOnlyList<ConsumerMetadata> GetAll();

    /// <summary>
    /// Finds a consumer by topic name and optional group.
    /// </summary>
    /// <param name="topic">The topic name to search for.</param>
    /// <param name="group">Optional consumer group name. If null, returns first match by topic only.</param>
    /// <returns>
    /// The matching consumer metadata, or null if no consumer is registered for the topic/group combination.
    /// </returns>
    /// <remarks>
    /// When multiple consumers are registered for the same topic with different groups,
    /// the group parameter must be specified to disambiguate.
    /// </remarks>
    ConsumerMetadata? FindByTopic(string topic, string? group = null);

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to search for.</typeparam>
    /// <returns>An enumerable of consumer metadata for all consumers handling the specified message type.</returns>
    /// <remarks>
    /// Multiple consumers can handle the same message type if they subscribe to different topics
    /// or belong to different consumer groups.
    /// </remarks>
    IEnumerable<ConsumerMetadata> FindByMessageType<TMessage>();

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <param name="messageType">The message type to search for.</param>
    /// <returns>An enumerable of consumer metadata for all consumers handling the specified message type.</returns>
    /// <remarks>
    /// Multiple consumers can handle the same message type if they subscribe to different topics
    /// or belong to different consumer groups.
    /// </remarks>
    IEnumerable<ConsumerMetadata> FindByMessageType(Type messageType);
}
