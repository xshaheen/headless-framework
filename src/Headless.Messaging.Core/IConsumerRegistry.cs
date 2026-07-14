// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

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
[PublicAPI]
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
    /// Finds a consumer by message name and optional group.
    /// </summary>
    /// <param name="messageName">The message name to search for.</param>
    /// <param name="group">Optional consumer group name. If null, returns first match by message name only.</param>
    /// <returns>
    /// The matching consumer metadata, or null if no consumer is registered for the message-name/group combination.
    /// </returns>
    /// <remarks>
    /// When multiple consumers are registered for the same message name with different groups,
    /// the group parameter must be specified to disambiguate.
    /// </remarks>
    ConsumerMetadata? FindByMessageName(string messageName, string? group = null);

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to search for.</typeparam>
    /// <returns>An enumerable of consumer metadata for all consumers handling the specified message type.</returns>
    /// <remarks>
    /// Multiple consumers can handle the same message type if they subscribe to different message names
    /// or belong to different consumer groups.
    /// </remarks>
    IReadOnlyList<ConsumerMetadata> FindByMessageType<TMessage>();

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <param name="messageType">The message type to search for.</param>
    /// <returns>A read-only list of consumer metadata for all consumers handling the specified message type.</returns>
    /// <remarks>
    /// Multiple consumers can handle the same message type if they subscribe to different message names
    /// or belong to different consumer groups.
    /// </remarks>
    IReadOnlyList<ConsumerMetadata> FindByMessageType(Type messageType);

    /// <summary>
    /// Attempts to find the <strong>raw</strong> (un-prefixed) message-name mapping registered for a message type.
    /// </summary>
    /// <param name="messageType">The message type to search for.</param>
    /// <param name="messageName">The raw message name, when a mapping exists.</param>
    /// <returns><see langword="true"/> when a mapping exists; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// The returned value is the raw logical name as registered — it is <strong>not</strong> the wire/subscription
    /// name. The framework prepends the configured <c>MessageNamePrefix</c> (on <c>MessagingOptions</c>) before
    /// comparing names at dispatch. Callers that build a subscription key or compare against a delivered message
    /// name MUST apply that prefix themselves; using this value directly mismatches whenever a prefix is configured.
    /// For most introspection (validation, docs, debugging) the raw name is what you want.
    /// </remarks>
    bool TryGetRawMessageName(Type messageType, [NotNullWhen(true)] out string? messageName)
    {
        messageName = null;
        return false;
    }
}
