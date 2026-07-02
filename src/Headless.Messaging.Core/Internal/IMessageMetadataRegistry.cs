// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Registration;

namespace Headless.Messaging.Internal;

internal interface IMessageMetadataRegistry
{
    bool TryGet(Type messageType, [NotNullWhen(true)] out MessageMetadata? metadata);
}

internal sealed record MessageMetadata(
    Type MessageType,
    Func<object, string?>? CorrelationSelector,
    IReadOnlyDictionary<Type, object> ProviderConfigs
);

internal sealed class MessageMetadataRegistry(IEnumerable<MessageRegistration> registrations) : IMessageMetadataRegistry
{
    private readonly Dictionary<Type, MessageMetadata> _metadataByType = _Build(registrations);
    private readonly ConcurrentDictionary<Type, MessageMetadata?> _resolvedMetadata = new();

    public bool TryGet(Type messageType, [NotNullWhen(true)] out MessageMetadata? metadata)
    {
        if (_resolvedMetadata.TryGetValue(messageType, out metadata))
        {
            return metadata is not null;
        }

        metadata = _resolvedMetadata.GetOrAdd(messageType, static (type, registry) => registry._Resolve(type), this);
        return metadata is not null;
    }

    private MessageMetadata? _Resolve(Type messageType)
    {
        if (_metadataByType.TryGetValue(messageType, out var exact))
        {
            return exact;
        }

        var candidates = _metadataByType
            .Where(pair => pair.Key.IsAssignableFrom(messageType))
            .Select(static pair => pair.Value)
            .ToArray();

        return candidates.Length switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                $"Message type '{messageType.FullName ?? messageType.Name}' matches multiple registered metadata types: "
                    + string.Join(
                        ", ",
                        candidates
                            .Select(static candidate => candidate.MessageType.FullName ?? candidate.MessageType.Name)
                            .Order(StringComparer.Ordinal)
                    )
                    + ". Register a more specific message metadata mapping or publish using an exact message type."
            ),
        };
    }

    private static Dictionary<Type, MessageMetadata> _Build(IEnumerable<MessageRegistration> registrations)
    {
        var metadata = new Dictionary<Type, MessageMetadata>();

        foreach (var group in registrations.GroupBy(static registration => registration.MessageType))
        {
            var correlationSelector = _MergeCorrelationSelector(group);
            var providerConfigs = _MergeProviderConfigs(group);
            metadata[group.Key] = new MessageMetadata(group.Key, correlationSelector, providerConfigs);
        }

        return metadata;
    }

    private static Func<object, string?>? _MergeCorrelationSelector(IEnumerable<MessageRegistration> registrations)
    {
        Func<object, string?>? selector = null;

        foreach (var registration in registrations)
        {
            if (registration.CorrelationSelector is null)
            {
                continue;
            }

            // Delegate semantic equality is not knowable here. Registration order is deterministic,
            // so repeated metadata blocks use the last explicit selector as the effective override.
            selector = registration.CorrelationSelector;
        }

        return selector;
    }

    private static Dictionary<Type, object> _MergeProviderConfigs(IEnumerable<MessageRegistration> registrations)
    {
        var configs = new Dictionary<Type, object>();

        foreach (var registration in registrations)
        {
            foreach (var pair in registration.ProviderConfigs)
            {
                // Provider config objects can contain delegates, so reference/value equality is not a
                // reliable conflict detector. Registration order is deterministic; later message-level
                // config of the same provider type intentionally replaces earlier config.
                configs[pair.Key] = pair.Value;
            }
        }

        return configs;
    }
}
