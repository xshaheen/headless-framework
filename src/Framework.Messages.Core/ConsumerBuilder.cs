// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Messages;

/// <summary>
/// Provides a fluent API for configuring individual consumer behavior.
/// </summary>
/// <typeparam name="TConsumer">The consumer type being configured.</typeparam>
internal sealed class ConsumerBuilder<TConsumer> : IConsumerBuilder<TConsumer>
    where TConsumer : class
{
    private readonly MessagingBuilder _parent;
    private readonly Type _messageType;
    private string? _topic;
    private string? _group;
    private byte _concurrency = 1;

    internal ConsumerBuilder(MessagingBuilder parent, Type messageType, string? topic = null)
    {
        _parent = parent;
        _messageType = messageType;
        _topic = topic;
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
        return this;
    }

    /// <inheritdoc />
    public IMessagingBuilder Build()
    {
        // Register the consumer metadata with the parent builder
        _parent.RegisterConsumer(typeof(TConsumer), _messageType, _topic, _group, _concurrency);
        return _parent;
    }
}
