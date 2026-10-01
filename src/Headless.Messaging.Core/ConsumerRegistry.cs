// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Headless.Messaging.Runtime;

namespace Headless.Messaging;

/// <summary>
/// Central registry for all registered message consumers.
/// </summary>
/// <remarks>
/// <para>
/// The registry stores metadata for every consumer the host's generated modules declare.
/// This metadata is used by <see cref="IConsumerServiceSelector"/> during startup to discover
/// and configure message subscriptions. The registry is registered as a singleton in DI.
/// </para>
/// <para>
/// One registry exists per service provider. The container builds it on first resolution, at startup or at an earlier
/// publish, by folding every message declaration, generated module, tuning, and <c>ConsumeOnly</c> entry recorded in the
/// service collection, then freezes it, so every reader sees the same names and consumers. Registration methods exist
/// for that fold; once <see cref="GetAll"/> is called, the registry is frozen and subsequent registrations throw.
/// </para>
/// </remarks>
internal sealed class ConsumerRegistry : IConsumerRegistry
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, string> _messageNameMappings = [];
    private readonly Dictionary<(Type MessageType, MessageLane Lane), string> _laneMessageNameMappings = [];
    private List<ConsumerMetadata>? _consumers = [];

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
        _ValidateDurableContract(metadata);

        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException(
                    "Cannot register consumers after the registry has been frozen. "
                        + "Ensure all consumers are registered during configuration before the application starts."
                );
            }

            _ThrowOnOwnershipConflict(_consumers!, metadata);

            var existingConflict = _FindDuplicateSubscriptionConflict(_consumers!, metadata);

            if (existingConflict != null)
            {
                throw new InvalidOperationException(
                    "Duplicate consumer registration detected for message name and subscription: "
                        + $"lane='{metadata.Lane}', messageName='{metadata.MessageName}', "
                        + $"subscription='{metadata.SubscriptionName}', existing consumer {_Describe(existingConflict)}, "
                        + $"new consumer {_Describe(metadata)}."
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

    internal void RegisterMessageName(Type messageType, MessageLane lane, string messageName)
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

            var key = (messageType, lane);
            if (
                _laneMessageNameMappings.TryGetValue(key, out var existingMessageName)
                && !string.Equals(existingMessageName, messageName, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new InvalidOperationException(
                    $"Message type {messageType.Name} is already mapped on lane {lane} to messageName '{existingMessageName}'. Cannot map to '{messageName}'."
                );
            }

            _laneMessageNameMappings[key] = messageName;
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
        _ValidateDurableContract(newMetadata);

        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException("Cannot update consumers after the registry has been frozen.");
            }

            var index = _consumers!.FindIndex(m => predicate(m));
            if (index >= 0)
            {
                _ThrowOnOwnershipConflict(_consumers!, newMetadata, index);

                var existingConflict = _FindDuplicateSubscriptionConflict(_consumers!, newMetadata, index);
                if (existingConflict != null)
                {
                    throw new InvalidOperationException(
                        "Duplicate consumer registration detected for message name and subscription: "
                            + $"lane='{newMetadata.Lane}', messageName='{newMetadata.MessageName}', "
                            + $"subscription='{newMetadata.SubscriptionName}', existing consumer "
                            + $"{_Describe(existingConflict)}, new consumer {_Describe(newMetadata)}."
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

    /// <summary>Finds a consumer by message name and optional subscription name.</summary>
    /// <param name="messageName">The message name to search for.</param>
    /// <param name="subscriptionName">
    /// Optional subscription name, the consumer identity on the Bus lane. If null, returns the first match by message
    /// name only.
    /// </param>
    /// <returns>
    /// The matching consumer metadata, or null if no consumer is registered for the message name and subscription.
    /// </returns>
    public ConsumerMetadata? FindByMessageName(string messageName, string? subscriptionName = null)
    {
        var all = GetAll();

        if (subscriptionName is null)
        {
            return all.FirstOrDefault(m =>
                string.Equals(m.MessageName, messageName, StringComparison.OrdinalIgnoreCase)
            );
        }

        return all.FirstOrDefault(m =>
            string.Equals(m.MessageName, messageName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.SubscriptionName, subscriptionName, StringComparison.Ordinal)
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

    public bool TryGetRawMessageName(Type messageType, MessageLane lane, [NotNullWhen(true)] out string? messageName)
    {
        Argument.IsNotNull(messageType);

        if (_frozen != null)
        {
            return _laneMessageNameMappings.TryGetValue((messageType, lane), out messageName)
                || _messageNameMappings.TryGetValue(messageType, out messageName);
        }

        lock (_lock)
        {
            return _laneMessageNameMappings.TryGetValue((messageType, lane), out messageName)
                || _messageNameMappings.TryGetValue(messageType, out messageName);
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

    internal IReadOnlyDictionary<(Type MessageType, MessageLane Lane), string> GetLaneMessageNameMappings()
    {
        if (_frozen != null)
        {
            return _laneMessageNameMappings;
        }

        lock (_lock)
        {
            return new Dictionary<(Type MessageType, MessageLane Lane), string>(_laneMessageNameMappings);
        }
    }

    /// <summary>Finds a consumer by consumer type and message type without freezing the registry.</summary>
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

    /// <summary>
    /// The consumers this host starts clients for, resolved from <c>ConsumeOnly</c> when the registry is built.
    /// Filtered-out consumers stay registered, so the host can still publish their messages.
    /// </summary>
    internal MessagingConsumeFilter ConsumeFilter { get; private set; } = MessagingConsumeFilter.All;

    /// <summary>One contract per declared message type, folded from every declaration of the host.</summary>
    internal IReadOnlyList<MessageContract> Contracts { get; private set; } = [];

    /// <summary>The route each declared contract contributes to each lane.</summary>
    internal IReadOnlyList<MessageRegistration> DeclaredRoutes { get; private set; } = [];

    /// <summary>The circuit-breaker overrides that tuning set on this host's consumers.</summary>
    internal ConsumerCircuitBreakerRegistry CircuitBreakers { get; } = new();

    /// <summary>Records what the host's declarations folded into, then freezes the registry.</summary>
    internal void Complete(MessageDeclarationFold declarations, MessagingConsumeFilter consumeFilter)
    {
        Argument.IsNotNull(declarations);
        Argument.IsNotNull(consumeFilter);

        lock (_lock)
        {
            if (_frozen != null)
            {
                throw new InvalidOperationException("Cannot complete the consumer registry after it has been frozen.");
            }

            Contracts = declarations.Contracts;
            DeclaredRoutes = declarations.Routes;
            ConsumeFilter = consumeFilter;
        }

        _ = GetAll();
    }

    private static ConsumerMetadata? _FindDuplicateSubscriptionConflict(
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
                // Message names match case-insensitively at dispatch; subscription names stay case-sensitive.
                string.Equals(existing.MessageName, candidate.MessageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.SubscriptionName, candidate.SubscriptionName, StringComparison.Ordinal)
                && existing.Lane == candidate.Lane
            )
            {
                return existing;
            }

            index++;
        }

        return null;
    }

    /// <summary>
    /// Rejects a consumer that would share an identity, a Queue message, or a durable route with a different
    /// registration. The checks run in this order so the error names the most specific rule that was broken.
    /// </summary>
    private static void _ThrowOnOwnershipConflict(
        List<ConsumerMetadata> consumers,
        ConsumerMetadata candidate,
        int? skipIndex = null
    )
    {
        foreach (var existing in _Others(consumers, skipIndex))
        {
            if (existing.Lane != candidate.Lane)
            {
                continue;
            }

            var sameIdentity = string.Equals(
                existing.ConsumerIdentity,
                candidate.ConsumerIdentity,
                StringComparison.Ordinal
            );

            // The route key alone would let two classes that share an identity but consume different messages share
            // one subscription, so an identity must belong to exactly one consumer class.
            if (sameIdentity && existing.ConsumerType != candidate.ConsumerType)
            {
                throw new InvalidOperationException(
                    $"Consumer identity '{candidate.ConsumerIdentity}' on lane {candidate.Lane} is declared by two "
                        + $"consumer classes: {_Describe(existing)} and {_Describe(candidate)}. An identity belongs to "
                        + "exactly one consumer class; give one of them a different identity."
                );
            }
        }

        foreach (var existing in _Others(consumers, skipIndex))
        {
            // Queue destinations are keyed by the message name, so a second consumer would compete for the same queue.
            if (
                candidate.Lane == MessageLane.Queue
                && existing.Lane == MessageLane.Queue
                && string.Equals(existing.MessageName, candidate.MessageName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(existing.ConsumerIdentity, candidate.ConsumerIdentity, StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    $"Queue message '{candidate.MessageName}' has two consumers: {_Describe(existing)} and "
                        + $"{_Describe(candidate)}. A message has at most one Queue consumer; remove one of them or "
                        + "consume the message on the Bus lane."
                );
            }
        }

        foreach (var existing in _Others(consumers, skipIndex))
        {
            // One identity may cover several messages, and the inbox keys rows by message name, so the durable route
            // is the identity plus the message name and its contract version on one lane.
            if (
                existing.Lane == candidate.Lane
                && string.Equals(existing.ConsumerIdentity, candidate.ConsumerIdentity, StringComparison.Ordinal)
                && string.Equals(existing.MessageName, candidate.MessageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    existing.MessageContractVersion,
                    candidate.MessageContractVersion,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidOperationException(
                    $"Duplicate durable consumer identity '{candidate.ConsumerIdentity}' for lane {candidate.Lane}, "
                        + $"message '{candidate.MessageName}', and message contract version "
                        + $"'{candidate.MessageContractVersion}'. Existing consumer {_Describe(existing)} conflicts "
                        + $"with {_Describe(candidate)}."
                );
            }
        }
    }

    /// <summary>Every registered consumer except the one at <paramref name="skipIndex"/>, the entry an update replaces.</summary>
    private static IEnumerable<ConsumerMetadata> _Others(List<ConsumerMetadata> consumers, int? skipIndex)
    {
        for (var index = 0; index < consumers.Count; index++)
        {
            if (index != skipIndex)
            {
                yield return consumers[index];
            }
        }
    }

    // Names the class and, for a generated consumer, the module that declared it, so a cross-module conflict points at
    // both sources.
    private static string _Describe(ConsumerMetadata metadata)
    {
        var type = metadata.ConsumerType.FullName ?? metadata.ConsumerType.Name;
        return metadata.DeclaringModule is { } module
            ? $"'{metadata.ConsumerIdentity}' ({type} in {module})"
            : $"'{metadata.ConsumerIdentity}' ({type})";
    }

    private static void _ValidateDurableContract(ConsumerMetadata metadata)
    {
        Argument.IsNotNull(metadata);

        if (string.IsNullOrWhiteSpace(metadata.ConsumerIdentity))
        {
            throw new ArgumentException("Consumer identity cannot be null or whitespace.", nameof(metadata));
        }

        MessagingOptions.ValidateContractVersion(metadata.MessageContractVersion);

        if (
            metadata.InboxRetention <= TimeSpan.Zero
            || metadata.InboxRetention.Ticks % TimeSpan.TicksPerSecond != 0
            || metadata.InboxRetention.TotalSeconds > int.MaxValue
        )
        {
            throw new ArgumentException(
                "Inbox retention must be a positive whole-second duration no greater than Int32.MaxValue seconds.",
                nameof(metadata)
            );
        }
    }
}
