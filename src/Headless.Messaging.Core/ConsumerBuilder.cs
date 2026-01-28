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
    private byte _concurrency = 1;

    internal ConsumerBuilder(
        MessagingOptions parent,
        ConsumerRegistry registry,
        Type messageType,
        string? _topic = null,
        bool autoRegistered = false
    )
    {
        _parent = parent;
        _registry = registry;
        _messageType = messageType;
        this._topic = _topic;
        _autoRegistered = autoRegistered;
    }

    /// <inheritdoc />
    public IConsumerBuilder<TConsumer> Topic(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        _topic = topic;
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
    public IConsumerBuilder<TConsumer> WithConcurrency(byte maxConcurrent)
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
    public IMessagingBuilder Build()
    {
        // If already auto-registered, just update with final settings
        if (_autoRegistered)
        {
            _UpdateIfAutoRegistered();
        }
        else
        {
            // Register the consumer metadata with the parent builder
            _parent.RegisterConsumer(typeof(TConsumer), _messageType, _topic, _group, _concurrency);
        }

        return _parent;
    }

    private void _UpdateIfAutoRegistered()
    {
        if (!_autoRegistered)
        {
            return;
        }

        // Determine final topic
        var finalTopic = _topic ?? _messageType.Name;

        // Update the existing registration
        _registry.Update(
            m => m.ConsumerType == typeof(TConsumer) && m.MessageType == _messageType,
            new ConsumerMetadata(_messageType, typeof(TConsumer), finalTopic, _group, _concurrency)
        );
    }
}
