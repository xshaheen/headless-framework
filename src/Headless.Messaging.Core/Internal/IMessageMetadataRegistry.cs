// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

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
        metadata = _resolvedMetadata.GetOrAdd(messageType, _Resolve);
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
            var correlationSelector = _MergeCorrelationSelector(group.Key, group);
            var providerConfigs = _MergeProviderConfigs(group.Key, group);
            metadata[group.Key] = new MessageMetadata(group.Key, correlationSelector, providerConfigs);
        }

        return metadata;
    }

    private static Func<object, string?>? _MergeCorrelationSelector(
        Type messageType,
        IEnumerable<MessageRegistration> registrations
    )
    {
        Func<object, string?>? selector = null;

        foreach (var registration in registrations)
        {
            if (registration.CorrelationSelector is null)
            {
                continue;
            }

            if (selector is null)
            {
                selector = registration.CorrelationSelector;
                continue;
            }

            if (!ReferenceEquals(selector, registration.CorrelationSelector))
            {
                throw new InvalidOperationException(
                    $"Message type '{messageType.FullName ?? messageType.Name}' has conflicting CorrelationFrom selectors."
                );
            }
        }

        return selector;
    }

    private static IReadOnlyDictionary<Type, object> _MergeProviderConfigs(
        Type messageType,
        IEnumerable<MessageRegistration> registrations
    )
    {
        var configs = new Dictionary<Type, object>();

        foreach (var registration in registrations)
        {
            foreach (var pair in registration.ProviderConfigs)
            {
                if (!configs.TryGetValue(pair.Key, out var existing))
                {
                    configs[pair.Key] = pair.Value;
                    continue;
                }

                if (!Equals(existing, pair.Value))
                {
                    throw new InvalidOperationException(
                        $"Message type '{messageType.FullName ?? messageType.Name}' has conflicting provider config "
                            + $"'{pair.Key.FullName ?? pair.Key.Name}'."
                    );
                }
            }
        }

        return configs;
    }
}
