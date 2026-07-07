// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging.Kafka;

/// <summary>Extension methods that attach Kafka provider-specific options to a message or consumer registration.</summary>
[PublicAPI]
public static class KafkaMessageBuilderExtensions
{
    /// <summary>
    /// Configures Kafka publish options for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The message builder.</param>
    /// <param name="configure">A delegate that configures the Kafka publish options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Requires the framework-provided <see cref="IMessageBuilder{TMessage}"/> from
    /// <c>setup.ForMessage&lt;TMessage&gt;</c>; custom or mocked builder implementations are not supported.
    /// </remarks>
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

    /// <summary>
    /// Configures Kafka consumer options for a bus consumer registration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type being registered.</typeparam>
    /// <param name="builder">The bus consumer builder.</param>
    /// <param name="configure">A delegate that configures the Kafka consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusConsumerBuilder<TConsumer> UseKafka<TConsumer>(
        this IBusConsumerBuilder<TConsumer> builder,
        Action<KafkaConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

    /// <summary>
    /// Configures Kafka consumer options for a queue consumer registration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type being registered.</typeparam>
    /// <param name="builder">The queue consumer builder.</param>
    /// <param name="configure">A delegate that configures the Kafka consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
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

/// <summary>Fluent builder for Kafka publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class KafkaMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _partitionSelector;

    /// <summary>
    /// Sets the Kafka message key from the outgoing message payload, which controls partition routing.
    /// Messages with the same key are guaranteed to land on the same partition and are delivered in order.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the partition key from the message instance.
    /// Return <see langword="null"/> to use round-robin partition assignment.
    /// Exceptions thrown by the selector are propagated to the caller at publish time.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public KafkaMessageConfigBuilder<TMessage> PartitionBy(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _partitionSelector = selector;
        return this;
    }

    internal KafkaMessageConfig<TMessage> Build() => new(_partitionSelector);
}

/// <summary>Fluent builder for Kafka consumer options applied to a consumer registration.</summary>
[PublicAPI]
public sealed class KafkaConsumerConfigBuilder
{
    private IsolationLevel? _isolationLevel;

    /// <summary>
    /// Sets the Kafka consumer <see cref="IsolationLevel"/>, controlling whether the consumer
    /// reads uncommitted or only committed transactional messages.
    /// When not set the Kafka client default (<see cref="IsolationLevel.ReadUncommitted"/>) is used.
    /// </summary>
    /// <param name="isolationLevel">The desired isolation level.</param>
    /// <returns>The same builder for chaining.</returns>
    public KafkaConsumerConfigBuilder WithIsolationLevel(IsolationLevel isolationLevel)
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
    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        partitionSelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    KafkaMessagingHeaders.KafkaKey,
                    message => partitionSelector((TMessage)message)
                ),
            ];
}
