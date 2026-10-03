// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Checks;
using Headless.Messaging.Kafka;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Extension methods that attach Kafka provider-specific options to a message contract or a tuned consumer.</summary>
[PublicAPI]
public static class KafkaMessageBuilderExtensions
{
    /// <summary>Configures Kafka publish options for <typeparamref name="TMessage"/> on the queue lane.</summary>
    public static IQueueContractBuilder<TMessage> UseKafka<TMessage>(
        this IQueueContractBuilder<TMessage> builder,
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

    /// <summary>
    /// Configures Kafka consumer options for the declared consumer a <c>Tune</c> call targets.
    /// </summary>
    /// <param name="builder">The consumer tuning builder.</param>
    /// <param name="configure">A delegate that configures the Kafka consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ConsumerTuningBuilder UseKafka(
        this ConsumerTuningBuilder builder,
        Action<KafkaConsumerConfigBuilder> configure
    )
    {
        _SetConsumerConfig(builder, configure);
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
