// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.RabbitMq;

[PublicAPI]
public static class RabbitMqMessageBuilderExtensions
{
    public static IBusConsumerBuilder<TConsumer> UseRabbitMq<TConsumer>(
        this IBusConsumerBuilder<TConsumer> builder,
        Action<RabbitMqConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

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

[PublicAPI]
public sealed class RabbitMqConsumerConfigBuilder
{
    private ushort? _prefetchCount;

    public RabbitMqConsumerConfigBuilder PrefetchCount(ushort prefetchCount)
    {
        _prefetchCount = prefetchCount;
        return this;
    }

    internal RabbitMqConsumerConfig Build() => new(_prefetchCount);
}

internal sealed record RabbitMqConsumerConfig(ushort? PrefetchCount);
