// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Fluent builder for RabbitMQ consumer options applied to a tuned consumer.</summary>
[PublicAPI]
public sealed class RabbitMqConsumerConfigBuilder
{
    private ushort? _prefetchCount;

    /// <summary>
    /// Overrides the RabbitMQ <c>basicQos</c> prefetch count for this consumer.
    /// Controls how many unacknowledged messages the broker delivers to the consumer at once.
    /// When not set, the global channel prefetch configured in <c>RabbitMqMessagingOptions</c> is used.
    /// </summary>
    /// <param name="prefetchCount">The maximum number of unacknowledged messages to prefetch.</param>
    /// <returns>The same builder for chaining.</returns>
    public RabbitMqConsumerConfigBuilder PrefetchCount(ushort prefetchCount)
    {
        _prefetchCount = prefetchCount;
        return this;
    }

    internal RabbitMqConsumerConfig Build()
    {
        return new(_prefetchCount);
    }
}

internal sealed record RabbitMqConsumerConfig(ushort? PrefetchCount);
