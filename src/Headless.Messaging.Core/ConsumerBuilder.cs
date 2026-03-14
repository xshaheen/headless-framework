// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;

namespace Headless.Messaging;

/// <summary>
/// Provides a fluent API for configuring individual consumer behavior.
/// </summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
internal sealed class ConsumerBuilder<TConsumer> : IConsumerBuilder<TConsumer>
    where TConsumer : class
{
    private readonly MessagingOptions _parent;
    private readonly ConsumerRegistry _registry;
    private readonly Type _messageType;
    private readonly bool _autoRegistered;
    private string? _topic;
    private string? _group;
    private string? _handlerId;
    private byte _concurrency = 1;

    internal ConsumerBuilder(
        MessagingOptions parent,
        ConsumerRegistry registry,
        Type messageType,
        string? topic = null,
        bool autoRegistered = false
    )
    {
        _parent = parent;
        _registry = registry;
        _messageType = messageType;
        this._topic = topic;
        _autoRegistered = autoRegistered;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Topic(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        _topic = topic;
        _UpdateIfAutoRegistered();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Group(string group)
    {
        Argument.IsNotNullOrWhiteSpace(group);

        _group = group;
        _UpdateIfAutoRegistered();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Concurrency(byte maxConcurrent)
    {
        if (maxConcurrent == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "Concurrency must be greater than 0");
        }

        _concurrency = maxConcurrent;
        _UpdateIfAutoRegistered();
        return this;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> HandlerId(string handlerId)
    {
        Argument.IsNotNullOrWhiteSpace(handlerId);

        _handlerId = handlerId;
        _UpdateIfAutoRegistered();
        return this;
    }

    private void _UpdateIfAutoRegistered()
    {
        if (!_autoRegistered)
        {
            return;
        }

        _registry.Update(
            m => m.ConsumerType == typeof(TConsumer) && m.MessageType == _messageType,
            _parent.CreateConsumerMetadata(
                typeof(TConsumer),
                _messageType,
                _topic,
                _group,
                _concurrency,
                _handlerId
            )
        );
    }
}
