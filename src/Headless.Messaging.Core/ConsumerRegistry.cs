// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Runtime;

namespace Headless.Messaging;

/// <summary>
/// Central registry for all registered message consumers.
/// </summary>
/// <remarks>
/// <para>
/// The registry stores metadata for all consumers registered via <see cref="IMessagingBuilder"/>.
/// This metadata is used by <see cref="IConsumerServiceSelector"/> during startup to discover
/// and configure message subscriptions. The registry is registered as a singleton in DI.
/// </para>
/// <para>
/// Thread-safety: Registration is expected during configuration phase (single-threaded).
/// Once <see cref="GetAll"/> is called, the registry is frozen and subsequent registrations throw.
/// This freeze-on-first-read pattern ensures zero-allocation reads at runtime.
/// </para>
/// </remarks>
internal sealed class ConsumerRegistry : IConsumerRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, string> _messageNameMappings = [];
    private List<ConsumerMetadata>? _consumers = [];
    private bool MessageRegistrationsDrained { get; set; }

    // volatile is required by the double-checked locking in GetAll: the unsynchronized first
    // read must observe a fully-published reference (not a partially-initialized AsReadOnly
    // wrapper) after the writer thread's lock-protected assignment.
    private volatile IReadOnlyList<ConsumerMetadata>? _frozen;

    /// <summary>
    /// Registers a consumer's metadata in the registry.
    /// </summary>
    /// <param name="metadata">The consumer metadata to register.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if registration is attempted after the registry has been frozen (after first <see cref="GetAll"/> call).
    /// </exception>
    public void Register(ConsumerMetadata metadata)
    {
        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException(
                    "Cannot register consumers after the registry has been frozen. "
                        + "Ensure all consumers are registered during configuration before the application starts."
                );
            }

            var existingConflict = _FindDuplicateTopicGroupConflict(_consumers!, metadata);

            if (existingConflict != null)
            {
                throw new InvalidOperationException(
                    "Duplicate consumer registration detected for messageName/group identity: "
                        + $"intent='{metadata.IntentType}', messageName='{metadata.MessageName}', group='{metadata.Group ?? "<default>"}', "
                        + $"existingHandlerId='{existingConflict.ResolvedHandlerId}', "
                        + $"newHandlerId='{metadata.ResolvedHandlerId}'."
                );
            }

            _consumers!.Add(metadata);
        }
    }

    /// <summary>
    /// Registers a raw message-name mapping for a message type.
    /// </summary>
    internal void RegisterMessageName(Type messageType, string messageName)
    {
        Argument.IsNotNull(messageType);
        MessagingOptions.ValidateMessageName(messageName);

        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException(
                    "Cannot register message-name mappings after the registry has been frozen. "
                        + "Ensure all mappings are registered during configuration before the application starts."
                );
            }

            if (
                _messageNameMappings.TryGetValue(messageType, out var existingMessageName)
                && !string.Equals(existingMessageName, messageName, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException(
                    $"Message type {messageType.Name} is already mapped to messageName '{existingMessageName}'. Cannot map to '{messageName}'."
                );
            }

            _messageNameMappings[messageType] = messageName;
        }
    }

    /// <summary>
    /// Updates consumer metadata matching the predicate.
    /// </summary>
    /// <param name="predicate">Predicate to find the metadata to update.</param>
    /// <param name="newMetadata">New metadata to replace with.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if update is attempted after the registry has been frozen.
    /// </exception>
    public void Update(Func<ConsumerMetadata, bool> predicate, ConsumerMetadata newMetadata)
    {
        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException("Cannot update consumers after the registry has been frozen.");
            }

            var index = _consumers!.FindIndex(m => predicate(m));
            if (index >= 0)
            {
                var existingConflict = _FindDuplicateTopicGroupConflict(_consumers!, newMetadata, index);
                if (existingConflict != null)
                {
                    throw new InvalidOperationException(
                        "Duplicate consumer registration detected for messageName/group identity: "
                            + $"intent='{newMetadata.IntentType}', messageName='{newMetadata.MessageName}', group='{newMetadata.Group ?? "<default>"}', "
                            + $"existingHandlerId='{existingConflict.ResolvedHandlerId}', "
                            + $"newHandlerId='{newMetadata.ResolvedHandlerId}'."
                    );
                }

                _consumers[index] = newMetadata;
            }
        }
    }

    /// <summary>
    /// Gets all registered consumer metadata.
    /// Freezes the registry on first call, preventing further registrations.
    /// </summary>
    /// <returns>A read-only list of all registered consumer metadata.</returns>
    public IReadOnlyList<ConsumerMetadata> GetAll()
    {
        if (_frozen != null)
        {
            return _frozen;
        }

        lock (_lock)
        {
#pragma warning disable CA1508 // Justification: other thread can initialize it
            if (_frozen == null)
#pragma warning restore CA1508
            {
                _frozen = _consumers!.AsReadOnly();
                _consumers = null; // Release for GC
            }
        }

        return _frozen;
    }

    /// <summary>
    /// Finds a consumer by message name and optional group.
    /// </summary>
    /// <param name="messageName">The message name to search for.</param>
    /// <param name="group">Optional consumer group name. If null, returns first match by message name only.</param>
    /// <returns>
    /// The matching consumer metadata, or null if no consumer is registered for the message-name/group combination.
    /// </returns>
    public ConsumerMetadata? FindByMessageName(string messageName, string? group = null)
    {
        var all = GetAll();

        if (group is null)
        {
            return all.FirstOrDefault(m =>
                string.Equals(m.MessageName, messageName, StringComparison.OrdinalIgnoreCase)
            );
        }

        return all.FirstOrDefault(m =>
            string.Equals(m.MessageName, messageName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Group, group, StringComparison.Ordinal)
        );
    }

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to search for.</typeparam>
    /// <returns>A read-only list of consumer metadata for all consumers handling the specified message type.</returns>
    public IReadOnlyList<ConsumerMetadata> FindByMessageType<TMessage>()
    {
        return FindByMessageType(typeof(TMessage));
    }

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <param name="messageType">The message type to search for.</param>
    /// <returns>A read-only list of consumer metadata for all consumers handling the specified message type.</returns>
    public IReadOnlyList<ConsumerMetadata> FindByMessageType(Type messageType)
    {
        var all = GetAll();
        return all.Where(m => m.MessageType == messageType).ToList().AsReadOnly();
    }

    public bool TryGetRawMessageName(Type messageType, [NotNullWhen(true)] out string? messageName)
    {
        Argument.IsNotNull(messageType);

        if (_frozen != null)
        {
            return _messageNameMappings.TryGetValue(messageType, out messageName);
        }

        lock (_lock)
        {
            return _messageNameMappings.TryGetValue(messageType, out messageName);
        }
    }

    internal IReadOnlyDictionary<Type, string> GetMessageNameMappings()
    {
        if (_frozen != null)
        {
            return _messageNameMappings;
        }

        lock (_lock)
        {
            return new Dictionary<Type, string>(_messageNameMappings);
        }
    }

    /// <summary>
    /// Finds a consumer by consumer type and message type without freezing the registry.
    /// Used internally during setup to resolve group names for deferred registrations.
    /// </summary>
    internal ConsumerMetadata? FindByTypes(Type consumerType, Type messageType)
    {
        if (_frozen != null)
        {
            return _frozen.FirstOrDefault(m => m.ConsumerType == consumerType && m.MessageType == messageType);
        }

        lock (_lock)
        {
            return _consumers?.FirstOrDefault(m => m.ConsumerType == consumerType && m.MessageType == messageType);
        }
    }

    /// <summary>
    /// Checks if a consumer is already registered for the specified message type.
    /// </summary>
    /// <param name="messageType">The message type to check.</param>
    /// <returns><see langword="true"/> if a consumer is registered for the message type; otherwise, <see langword="false"/>.</returns>
    public bool IsRegistered(Type messageType)
    {
        // If frozen, check the frozen list
        if (_frozen != null)
        {
            return _frozen.Any(m => m.MessageType == messageType);
        }

        // Otherwise check the mutable list
        lock (_lock)
        {
            return _consumers?.Exists(m => m.MessageType == messageType) ?? false;
        }
    }

    internal bool HasCompletedMessageRegistrationDrain
    {
        get
        {
            lock (_lock)
            {
                return MessageRegistrationsDrained;
            }
        }
    }

    internal void MarkMessageRegistrationDrainCompleted()
    {
        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException(
                    "Cannot drain message registrations after the registry has been frozen."
                );
            }

            MessageRegistrationsDrained = true;
        }
    }

    private static ConsumerMetadata? _FindDuplicateTopicGroupConflict(
        IEnumerable<ConsumerMetadata> consumers,
        ConsumerMetadata candidate,
        int? skipIndex = null
    )
    {
        var index = 0;
        foreach (var existing in consumers)
        {
            if (skipIndex.HasValue && index == skipIndex.Value)
            {
                index++;
                continue;
            }

            if (
                // Message names match case-insensitively at dispatch; groups stay case-sensitive.
                string.Equals(existing.MessageName, candidate.MessageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Group, candidate.Group, StringComparison.Ordinal)
                && existing.IntentType == candidate.IntentType
            )
            {
                return existing;
            }

            index++;
        }

        return null;
    }
}
