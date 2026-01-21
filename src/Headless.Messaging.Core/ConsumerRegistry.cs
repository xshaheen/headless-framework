// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

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
public sealed class ConsumerRegistry : IConsumerRegistry
{
    private readonly Lock _lock = new();
    private List<ConsumerMetadata>? _consumers = [];
    private IReadOnlyList<ConsumerMetadata>? _frozen;

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

            _consumers!.Add(metadata);
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
        if (_frozen == null)
        {
            lock (_lock)
            {
                if (_frozen == null)
                {
                    _frozen = _consumers!.AsReadOnly();
                    _consumers = null; // Release for GC
                }
            }
        }

        return _frozen;
    }

    /// <summary>
    /// Finds a consumer by topic name and optional group.
    /// </summary>
    /// <param name="topic">The topic name to search for.</param>
    /// <param name="group">Optional consumer group name. If null, returns first match by topic only.</param>
    /// <returns>
    /// The matching consumer metadata, or null if no consumer is registered for the topic/group combination.
    /// </returns>
    public ConsumerMetadata? FindByTopic(string topic, string? group = null)
    {
        var all = GetAll();

        if (group is null)
        {
            return all.FirstOrDefault(m => m.Topic == topic);
        }

        return all.FirstOrDefault(m => m.Topic == topic && m.Group == group);
    }

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to search for.</typeparam>
    /// <returns>An enumerable of consumer metadata for all consumers handling the specified message type.</returns>
    public IEnumerable<ConsumerMetadata> FindByMessageType<TMessage>()
    {
        return FindByMessageType(typeof(TMessage));
    }

    /// <summary>
    /// Finds all consumers that handle a specific message type.
    /// </summary>
    /// <param name="messageType">The message type to search for.</param>
    /// <returns>An enumerable of consumer metadata for all consumers handling the specified message type.</returns>
    public IEnumerable<ConsumerMetadata> FindByMessageType(Type messageType)
    {
        var all = GetAll();
        return all.Where(m => m.MessageType == messageType);
    }

    /// <summary>
    /// Checks if a consumer is already registered for the specified message type.
    /// </summary>
    /// <param name="messageType">The message type to check.</param>
    /// <returns><c>true</c> if a consumer is registered for the message type; otherwise, <c>false</c>.</returns>
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
            return _consumers?.Any(m => m.MessageType == messageType) ?? false;
        }
    }
}
