// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessageMetadataRegistry
{
    bool TryGet(MessageRouteKey route, [NotNullWhen(true)] out MessageMetadata? metadata);
}

internal sealed record MessageMetadata(
    MessageRouteKey Route,
    Type MessageType,
    Func<object, string?>? CorrelationSelector,
    IReadOnlyDictionary<Type, object> ProviderConfigs
);

internal sealed class MessageMetadataRegistry(
    IEnumerable<MessageRegistration> registrations,
    IConsumerRegistry? consumerRegistry = null,
    IOptions<MessagingOptions>? optionsAccessor = null
) : IMessageMetadataRegistry
{
    private readonly Dictionary<MessageRouteKey, MessageMetadata> _metadataByRoute = _Build(
        registrations,
        consumerRegistry,
        optionsAccessor?.Value
    );

    public bool TryGet(MessageRouteKey route, [NotNullWhen(true)] out MessageMetadata? metadata)
    {
        return _metadataByRoute.TryGetValue(route, out metadata);
    }

    private static Dictionary<MessageRouteKey, MessageMetadata> _Build(
        IEnumerable<MessageRegistration> registrations,
        IConsumerRegistry? consumerRegistry,
        MessagingOptions? options
    )
    {
        var metadata = new Dictionary<MessageRouteKey, MessageMetadata>();

        foreach (
            var group in registrations.GroupBy(registration =>
                (
                    registration.MessageType,
                    registration.Lane,
                    MessageName: _ResolveMessageName(registration, consumerRegistry, options)
                )
            )
        )
        {
            if (group.Key.MessageName is null)
            {
                continue;
            }

            var correlationSelector = _MergeCorrelationSelector(group);
            var providerConfigs = _MergeProviderConfigs(group);

            var route = new MessageRouteKey(group.Key.MessageType, group.Key.MessageName, group.Key.Lane);
            metadata[route] = new MessageMetadata(route, group.Key.MessageType, correlationSelector, providerConfigs);
        }

        return metadata;
    }

    private static string? _ResolveMessageName(
        MessageRegistration registration,
        IConsumerRegistry? consumerRegistry,
        MessagingOptions? options
    )
    {
        var rawName = registration.MessageName;

        if (
            string.IsNullOrWhiteSpace(rawName)
            && consumerRegistry?.TryGetRawMessageName(registration.MessageType, registration.Lane, out var mappedName)
                == true
        )
        {
            rawName = mappedName;
        }

        if (
            string.IsNullOrWhiteSpace(rawName)
            && options?.Conventions?.GetMessageName(registration.MessageType) is { } conventionName
        )
        {
            rawName = conventionName;
        }

        return string.IsNullOrWhiteSpace(rawName) || options is null
            ? rawName
            : options.ApplyMessageNamePrefix(rawName);
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
