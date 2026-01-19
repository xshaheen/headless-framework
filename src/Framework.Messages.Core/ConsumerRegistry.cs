// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <summary>
/// Central registry for all registered message consumers.
/// </summary>
/// <remarks>
/// The registry stores metadata for all consumers registered via <see cref="IMessagingBuilder"/>.
/// This metadata is used by <see cref="IConsumerServiceSelector"/> during startup to discover
/// and configure message subscriptions. The registry is registered as a singleton in DI.
/// </remarks>
internal sealed class ConsumerRegistry
{
    private readonly List<ConsumerMetadata> _consumers = [];

    /// <summary>
    /// Registers a consumer's metadata in the registry.
    /// </summary>
    /// <param name="metadata">The consumer metadata to register.</param>
    public void Register(ConsumerMetadata metadata)
    {
        _consumers.Add(metadata);
    }

    /// <summary>
    /// Gets all registered consumer metadata.
    /// </summary>
    /// <returns>A read-only list of all registered consumer metadata.</returns>
    public IReadOnlyList<ConsumerMetadata> GetAll() => _consumers;
}
