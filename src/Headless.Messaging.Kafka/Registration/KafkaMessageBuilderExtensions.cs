// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Checks;

namespace Headless.Messaging.Kafka;

[PublicAPI]
public static class KafkaMessageBuilderExtensions
{
    public static IMessageBuilder<TMessage> UseKafka<TMessage>(
        this IMessageBuilder<TMessage> builder,
        Action<KafkaMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new KafkaMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

    public static IBusConsumerBuilder<TConsumer> UseKafka<TConsumer>(
        this IBusConsumerBuilder<TConsumer> builder,
        Action<KafkaConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

    public static IQueueConsumerBuilder<TConsumer> UseKafka<TConsumer>(
        this IQueueConsumerBuilder<TConsumer> builder,
        Action<KafkaConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

    private static void _SetConsumerConfig(
        IConsumerProviderConfigBuilder builder,
        Action<KafkaConsumerConfigBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new KafkaConsumerConfigBuilder();
        configure(configBuilder);
        builder.SetConsumerProviderConfig(configBuilder.Build());
    }
}

[PublicAPI]
public sealed class KafkaMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _partitionSelector;

    public KafkaMessageConfigBuilder<TMessage> PartitionBy(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _partitionSelector = selector;
        return this;
    }

    internal KafkaMessageConfig<TMessage> Build() => new(_partitionSelector);
}

[PublicAPI]
public sealed class KafkaConsumerConfigBuilder
{
    private IsolationLevel? _isolationLevel;

    public KafkaConsumerConfigBuilder IsolationLevel(IsolationLevel isolationLevel)
    {
        _isolationLevel = isolationLevel;
        return this;
    }

    internal KafkaConsumerConfig Build() => new(_isolationLevel);
}

internal sealed record KafkaConsumerConfig(IsolationLevel? IsolationLevel);

internal sealed class KafkaMessageConfig<TMessage>(Func<TMessage, string?>? partitionSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions =>
        partitionSelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    KafkaHeaders.KafkaKey,
                    message => partitionSelector((TMessage)message)
                ),
            ];
}
