// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IRuntimeSubscriptionCatalog
{
    IReadOnlyList<ConsumerExecutorDescriptor> GetConsumerDescriptors();
}

internal sealed class RuntimeMessageSubscriber(
    IOptions<MessagingOptions> options,
    ConsumerRegistry consumerRegistry,
    MethodMatcherCache matcherCache,
    IConsumerRegister consumerRegister
) : IRuntimeMessageSubscriber, IRuntimeSubscriptionCatalog
{
    private readonly Lock _lock = new();
    private readonly MessagingOptions _options = options.Value;

    private readonly Dictionary<RuntimeSubscriptionKey, ConsumerExecutorDescriptor> _subscriptions = [];
    private IReadOnlyList<ConsumerExecutorDescriptor> _descriptors = [];
    private IReadOnlyList<RuntimeSubscriptionKey> _keys = [];

    public ValueTask<RuntimeSubscriptionKey> SubscribeAsync<TMessage>(
        RuntimeMessageHandler<TMessage> handler,
        string? topic = null,
        string? group = null,
        string? handlerId = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class
    {
        Argument.IsNotNull(handler);

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedTopic = _ResolveTopic(typeof(TMessage), topic);
        var resolvedGroup = _ResolveFullGroup(group);
        var resolvedHandlerId = _ResolveHandlerId(handler.Method, handlerId);
        var key = new RuntimeSubscriptionKey(typeof(TMessage), resolvedTopic, resolvedGroup, resolvedHandlerId);

        lock (_lock)
        {
            if (_subscriptions.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Runtime subscription already exists for '{key.Topic}' and group '{key.Group}' with handler id '{key.HandlerId}'."
                );
            }

            if (_IsRouteReservedByStaticSubscription(key.Topic, key.Group))
            {
                throw new InvalidOperationException(
                    $"Cannot register runtime subscription for '{key.Topic}' and group '{key.Group}' because the route is already used by a class consumer."
                );
            }

            if (_subscriptions.Values.Any(s => s.TopicName == key.Topic && s.GroupName == key.Group))
            {
                throw new InvalidOperationException(
                    $"Cannot register runtime subscription for '{key.Topic}' and group '{key.Group}' because runtime routes must be unique."
                );
            }

            var descriptor = _CreateDescriptor(key, (sp, context, ct) => handler(sp, (ConsumeContext<TMessage>)context, ct));
            _subscriptions[key] = descriptor;
            _RefreshSnapshots();
        }

        return _RefreshConsumersAsync(key, cancellationToken);
    }

    public ValueTask<RuntimeSubscriptionKey> SubscribeAsync<TMessage>(
        Func<ConsumeContext<TMessage>, CancellationToken, ValueTask> handler,
        string? topic = null,
        string? group = null,
        string? handlerId = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class
    {
        Argument.IsNotNull(handler);

        return SubscribeAsync<TMessage>(
            (serviceProvider, context, ct) =>
            {
                _ = serviceProvider;
                return handler(context, ct);
            },
            topic,
            group,
            handlerId,
            cancellationToken
        );
    }

    public async ValueTask<bool> UnsubscribeAsync(
        RuntimeSubscriptionKey subscriptionKey,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removed = false;
        lock (_lock)
        {
            removed = _subscriptions.Remove(subscriptionKey);
            if (removed)
            {
                _RefreshSnapshots();
            }
        }

        if (!removed)
        {
            return false;
        }

        await _RefreshConsumersAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask<bool> UnsubscribeAsync<TMessage>(
        string? topic = null,
        string? group = null,
        string? handlerId = null,
        CancellationToken cancellationToken = default
    )
        where TMessage : class
    {
        var resolvedTopic = _ResolveTopic(typeof(TMessage), topic);
        var resolvedGroup = _ResolveFullGroup(group);
        var subscriptions = _keys.Where(k =>
            k.MessageType == typeof(TMessage)
            && k.Topic.Equals(resolvedTopic, StringComparison.OrdinalIgnoreCase)
            && k.Group.Equals(resolvedGroup, StringComparison.Ordinal)
            && (handlerId is null || k.HandlerId.Equals(handlerId, StringComparison.Ordinal))
        );

        var match = subscriptions.ToList();
        return match.Count switch
        {
            0 => ValueTask.FromResult(false),
            1 => UnsubscribeAsync(match[0], cancellationToken),
            _ => ValueTask.FromException<bool>(
                new InvalidOperationException(
                    $"Multiple runtime subscriptions found for '{resolvedTopic}' and group '{resolvedGroup}'. Provide a handlerId."
                )
            ),
        };
    }

    public IReadOnlyList<RuntimeSubscriptionKey> ListSubscriptions()
    {
        return _keys;
    }

    public IReadOnlyList<ConsumerExecutorDescriptor> GetConsumerDescriptors()
    {
        return _descriptors;
    }

    private static string _ResolveHandlerId(MethodInfo method, string? handlerId)
    {
        if (!string.IsNullOrWhiteSpace(handlerId))
        {
            return handlerId;
        }

        return $"{method.DeclaringType?.FullName ?? "RuntimeHandler"}::{method.Name}";
    }

    private string _ResolveTopic(Type messageType, string? topic)
    {
        var resolved =
            topic
            ?? (_options.TopicMappings.TryGetValue(messageType, out var mappedTopic) ? mappedTopic : null)
            ?? _options.Conventions?.GetTopicName(messageType)
            ?? messageType.Name;

        if (_options.StrictValidation)
        {
            MessagingOptions.ValidateSubscriptionTopicName(resolved);
        }

        return resolved;
    }

    private string _ResolveFullGroup(string? group)
    {
        var baseGroup = group ?? _options.Conventions?.DefaultGroup ?? _options.DefaultGroupName;

        if (_options.StrictValidation)
        {
            MessagingOptions.ValidateGroupName(baseGroup);
        }

        var prefix = !string.IsNullOrEmpty(_options.GroupNamePrefix) ? $"{_options.GroupNamePrefix}." : string.Empty;
        return $"{prefix}{baseGroup}.{_options.Version}";
    }

    private bool _IsRouteReservedByStaticSubscription(string topic, string fullGroup)
    {
        foreach (var metadata in consumerRegistry.GetAll())
        {
            var group = _ResolveFullGroup(metadata.Group);
            if (
                metadata.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase)
                && group.Equals(fullGroup, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor(
        RuntimeSubscriptionKey key,
        Func<IServiceProvider, object, CancellationToken, ValueTask> handler
    )
    {
        var consumeMethod = typeof(IConsume<>).MakeGenericType(key.MessageType).GetMethod(nameof(IConsume<object>.Consume))!;
        var markerType = typeof(RuntimeMessageSubscriber);

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = markerType.GetTypeInfo(),
            ImplTypeInfo = markerType.GetTypeInfo(),
            MethodInfo = consumeMethod,
            TopicName = key.Topic,
            GroupName = key.Group,
            Parameters = consumeMethod
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name,
                    ParameterType = p.ParameterType,
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
                })
                .ToList(),
            RuntimeSubscriptionKey = key,
            RuntimeHandler = handler,
        };
    }

    private void _RefreshSnapshots()
    {
        _descriptors = _subscriptions.Values.ToList().AsReadOnly();
        _keys = _subscriptions.Keys.ToList().AsReadOnly();
    }

    private async ValueTask<RuntimeSubscriptionKey> _RefreshConsumersAsync(
        RuntimeSubscriptionKey key,
        CancellationToken cancellationToken = default
    )
    {
        await _RefreshConsumersAsync(cancellationToken).ConfigureAwait(false);
        return key;
    }

    private async ValueTask _RefreshConsumersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        matcherCache.Refresh();

        if (consumerRegister.IsStarted())
        {
            await consumerRegister.ReStartAsync(force: true).ConfigureAwait(false);
        }
    }
}
