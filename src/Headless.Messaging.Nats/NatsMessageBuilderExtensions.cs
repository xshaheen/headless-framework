// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging.Nats;

/// <summary>Extension methods that attach NATS JetStream provider-specific options to a message or consumer registration.</summary>
[PublicAPI]
public static class NatsMessageBuilderExtensions
{
    /// <summary>
    /// Configures NATS JetStream options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The message builder.</param>
    /// <param name="configure">A delegate that configures the NATS options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Requires the framework-provided <see cref="IMessageBuilder{TMessage}"/> from
    /// <c>setup.ForMessage&lt;TMessage&gt;</c>; custom or mocked builder implementations are not supported.
    /// </remarks>
    public static IMessageBuilder<TMessage> UseNats<TMessage>(
        this IMessageBuilder<TMessage> builder,
        Action<NatsMessageConfigBuilder<TMessage>> configure
    )
        where TMessage : class
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new NatsMessageConfigBuilder<TMessage>();
        configure(configBuilder);
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(configBuilder.Build());

        return builder;
    }

    /// <summary>
    /// Configures NATS JetStream consumer options for a bus consumer registration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type being registered.</typeparam>
    /// <param name="builder">The bus consumer builder.</param>
    /// <param name="configure">A delegate that configures the NATS consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusConsumerBuilder<TConsumer> UseNats<TConsumer>(
        this IBusConsumerBuilder<TConsumer> builder,
        Action<NatsConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

    /// <summary>
    /// Configures NATS JetStream consumer options for a queue consumer registration.
    /// </summary>
    /// <typeparam name="TConsumer">The consumer type being registered.</typeparam>
    /// <param name="builder">The queue consumer builder.</param>
    /// <param name="configure">A delegate that configures the NATS consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IQueueConsumerBuilder<TConsumer> UseNats<TConsumer>(
        this IQueueConsumerBuilder<TConsumer> builder,
        Action<NatsConsumerConfigBuilder> configure
    )
        where TConsumer : class
    {
        _SetConsumerConfig((IConsumerProviderConfigBuilder)builder, configure);
        return builder;
    }

    private static void _SetConsumerConfig(
        IConsumerProviderConfigBuilder builder,
        Action<NatsConsumerConfigBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new NatsConsumerConfigBuilder();
        configure(configBuilder);
        builder.SetConsumerProviderConfig(configBuilder.Build());
    }
}

/// <summary>Fluent builder for NATS JetStream publish options applied to a single message type.</summary>
/// <typeparam name="TMessage">The message type being configured.</typeparam>
[PublicAPI]
public sealed class NatsMessageConfigBuilder<TMessage>
    where TMessage : class
{
    private Func<TMessage, string?>? _subjectShardSelector;

    /// <summary>
    /// Appends a dynamic shard token to the NATS subject, producing <c>{subject}.{shard}</c>.
    /// Use this to fan messages across multiple stream subjects for horizontal scaling.
    /// </summary>
    /// <param name="selector">
    /// A delegate that derives the shard token from the message instance.
    /// The token must be a single safe NATS subject token: it cannot contain <c>.</c>, <c>*</c>,
    /// <c>&gt;</c>, whitespace, or control characters. Invalid values are silently ignored at
    /// publish time and the base subject is used instead (a warning is logged).
    /// Return <see langword="null"/> to skip sharding for this message.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public NatsMessageConfigBuilder<TMessage> SubjectShard(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _subjectShardSelector = selector;
        return this;
    }

    internal NatsMessageConfig<TMessage> Build() => new(_subjectShardSelector);
}

internal sealed class NatsMessageConfig<TMessage>(Func<TMessage, string?>? subjectShardSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        subjectShardSelector is null
            ? []
            :
            [
                new ProviderHeaderContribution(
                    NatsMessagingHeaders.SubjectShard,
                    message => NatsSubjectShard.Validate(subjectShardSelector((TMessage)message))
                ),
            ];
}

/// <summary>Fluent builder for NATS JetStream consumer options applied to a consumer registration.</summary>
[PublicAPI]
public sealed class NatsConsumerConfigBuilder
{
    private bool _isSharded;

    /// <summary>
    /// Declares that this consumer subscribes to sharded subjects (i.e. the producer uses
    /// <c>SubjectShard(...)</c>). When set, the consumer registers a <c>{subject}.&gt;</c>
    /// wildcard filter so that all shard tokens are received. Required whenever the producer
    /// is sharded; omitting it causes silent message loss because NATS delivers zero messages
    /// to a non-wildcard filter that does not match any shard subject.
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public NatsConsumerConfigBuilder Sharded()
    {
        _isSharded = true;
        return this;
    }

    internal NatsConsumerConfig Build() => new(_isSharded);
}

internal sealed record NatsConsumerConfig(bool IsSharded);
