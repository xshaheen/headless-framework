// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Extension methods that attach RabbitMQ provider-specific options to a consumer registration.</summary>
[PublicAPI]
public static class RabbitMqMessageBuilderExtensions
{
    /// <summary>
    /// Configures RabbitMQ consumer options for a bus consumer registration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type being registered.</typeparam>
    /// <param name="builder">The bus consumer builder.</param>
    /// <param name="configure">A delegate that configures the RabbitMQ consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusConsumerBuilder<TConsumer> UseRabbitMq<TConsumer>(
        this IBusConsumerBuilder<TConsumer> builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

    /// <summary>
    /// Configures RabbitMQ consumer options for a queue consumer registration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type being registered.</typeparam>
    /// <param name="builder">The queue consumer builder.</param>
    /// <param name="configure">A delegate that configures the RabbitMQ consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IQueueConsumerBuilder<TConsumer> UseRabbitMq<TConsumer>(
        this IQueueConsumerBuilder<TConsumer> builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
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

/// <summary>Fluent builder for RabbitMQ consumer options applied to a consumer registration.</summary>
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
