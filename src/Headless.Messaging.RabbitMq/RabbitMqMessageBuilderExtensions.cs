// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Extension methods that attach RabbitMQ provider-specific options to a tuned consumer.</summary>
[PublicAPI]
public static class RabbitMqMessageBuilderExtensions
{
    /// <summary>
    /// Configures RabbitMQ consumer options for the declared consumer a <c>Tune</c> call targets.
    /// </summary>
    /// <param name="builder">The consumer tuning builder.</param>
    /// <param name="configure">A delegate that configures the RabbitMQ consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ConsumerTuningBuilder UseRabbitMq(
        this ConsumerTuningBuilder builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
    {
        _SetConsumerConfig(builder, configure);
        return builder;
    }

    private static void _SetConsumerConfig(
        IConsumerProviderConfigBuilder builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new RabbitMqConsumerConfigBuilder();
        configure(configBuilder);
        builder.SetConsumerProviderConfig(configBuilder.Build());
    }
}

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
