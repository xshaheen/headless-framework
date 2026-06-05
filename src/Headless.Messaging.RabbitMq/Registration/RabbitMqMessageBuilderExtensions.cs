// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.RabbitMq;

[PublicAPI]
public static class RabbitMqMessageBuilderExtensions
{
    public static IMessageBuilder<TMessage> UseRabbitMq<TMessage>(
        this IMessageBuilder<TMessage> builder,
        Action<RabbitMqMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new RabbitMqMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

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
public sealed class RabbitMqMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private string? _exchangeType;
    private Func<TMessage, string?>? _routingKeySelector;

    public RabbitMqMessageConfigBuilder<TMessage> ExchangeType(string exchangeType)
    {
        Argument.IsNotNullOrWhiteSpace(exchangeType);

        _exchangeType = exchangeType;
        return this;
    }

    public RabbitMqMessageConfigBuilder<TMessage> RoutingKeyFromMessage(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _routingKeySelector = selector;
        return this;
    }

    internal RabbitMqMessageConfig<TMessage> Build() => new(_exchangeType, _routingKeySelector);
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

internal sealed class RabbitMqMessageConfig<TMessage>(
    string? exchangeType,
    Func<TMessage, string?>? routingKeySelector
) : IProviderHeaderContributions
    where TMessage : class
{
    public string? ExchangeType { get; } = exchangeType;

    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions =>
        routingKeySelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    RabbitMqHeaders.RoutingKey,
                    message => routingKeySelector((TMessage)message)
                ),
            ];
}
