// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Nats;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging;

/// <summary>Extension methods that attach NATS JetStream provider-specific options to a message contract or a tuned consumer.</summary>
[PublicAPI]
public static class NatsMessageBuilderExtensions
{
    /// <summary>
    /// Configures NATS JetStream options for <typeparamref name="TMessage"/> publish operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type being registered.</typeparam>
    /// <param name="builder">The Bus route of the message contract.</param>
    /// <param name="configure">A delegate that configures the NATS options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IBusContractBuilder<TMessage> UseNats<TMessage>(
        this IBusContractBuilder<TMessage> builder,
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

    /// <summary>Configures NATS JetStream options for <typeparamref name="TMessage"/> queue publish operations.</summary>
    public static IQueueContractBuilder<TMessage> UseNats<TMessage>(
        this IQueueContractBuilder<TMessage> builder,
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
    /// Configures NATS JetStream consumer options for the declared consumer a <c>Tune</c> call targets.
    /// </summary>
    /// <param name="builder">The consumer tuning builder.</param>
    /// <param name="configure">A delegate that configures the NATS JetStream consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ConsumerTuningBuilder UseNats(
        this ConsumerTuningBuilder builder,
        Action<NatsConsumerConfigBuilder> configure
    )
    {
        _SetConsumerConfig(builder, configure);
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
    /// The token must be a single safe NATS subject token: it must be non-empty, at most 256 characters,
    /// and cannot contain <c>.</c>, <c>*</c>, <c>&gt;</c>, whitespace, or control characters. A non-null
    /// token that violates any of these rules causes an <see cref="InvalidOperationException"/> to be
    /// thrown at publish time rather than being silently dropped.
    /// Return <see langword="null"/> to skip sharding for this message.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public NatsMessageConfigBuilder<TMessage> SubjectShard(Func<TMessage, string?> selector)
    {
        Argument.IsNotNull(selector);

        _subjectShardSelector = selector;
        return this;
    }

    internal NatsMessageConfig<TMessage> Build()
    {
        return new(_subjectShardSelector);
    }
}

internal sealed class NatsMessageConfig<TMessage>(Func<TMessage, string?>? subjectShardSelector)
    : IProviderHeaderContributions
    where TMessage : class
{
    // A message contract merges with an identical redeclaration from another module, so two configs holding the same
    // selector are equal and two different selectors conflict.
    private readonly Func<TMessage, string?>? _selector = subjectShardSelector;

    public override bool Equals(object? obj) =>
        obj is NatsMessageConfig<TMessage> other && Equals(_selector, other._selector);

    public override int GetHashCode() => _selector?.GetHashCode() ?? 0;

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
/// <remarks>
/// Each <c>UseNats(...)</c> call replaces the NATS settings of an earlier one for the same consumer, so set every NATS
/// option of a consumer in one call. The durable name, filter subject, and delivery policy stay provider-owned.
/// </remarks>
[PublicAPI]
public sealed class NatsConsumerConfigBuilder
{
    private bool _isSharded;
    private TimeSpan? _ackWait;
    private int? _maxAckPending;
    private int? _maxDeliver;
    private TimeSpan? _inactiveThreshold;

    /// <summary>
    /// Declares that this consumer subscribes to sharded subjects (i.e. the producer uses
    /// <c>SubjectShard(...)</c>). When set, the consumer registers a <c>{subject}.&gt;</c>
    /// wildcard filter so that all shard tokens are received.
    /// </summary>
    /// <remarks>
    /// A consumer of a message whose contract this host declares with <c>SubjectShard(...)</c> filters on the shard
    /// wildcard without this call. Call it when the producer shards a message that this host declares without the
    /// shard: NATS delivers zero messages to a non-wildcard filter that matches no shard subject.
    /// </remarks>
    /// <returns>The same builder for chaining.</returns>
    public NatsConsumerConfigBuilder Sharded()
    {
        _isSharded = true;
        return this;
    }

    /// <summary>
    /// Sets how long JetStream waits for a delivery's acknowledgement before it redelivers the message. Defaults to
    /// <c>30 seconds</c>.
    /// </summary>
    /// <remarks>
    /// The consumer reports a delivery in progress every half <c>AckWait</c> until it settles, so a slow receive stage
    /// does not cause a redelivery; a shorter value detects a crashed consumer sooner at the cost of more progress
    /// signals.
    /// </remarks>
    /// <param name="ackWait">The acknowledgement wait.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ackWait"/> is not positive.</exception>
    public NatsConsumerConfigBuilder AckWait(TimeSpan ackWait)
    {
        _ackWait = Argument.IsPositive(ackWait);
        return this;
    }

    /// <summary>
    /// Sets how many deliveries may wait for acknowledgement at once across every instance of this consumer; JetStream
    /// stops delivering until one settles. Defaults to the server's limit (<c>1000</c>).
    /// </summary>
    /// <param name="maxAckPending">The most unacknowledged deliveries.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAckPending"/> is not positive.</exception>
    public NatsConsumerConfigBuilder MaxAckPending(int maxAckPending)
    {
        _maxAckPending = Argument.IsPositive(maxAckPending);
        return this;
    }

    /// <summary>
    /// Sets how many times JetStream delivers a message before it stops redelivering it. Defaults to no limit.
    /// </summary>
    /// <remarks>
    /// The transport rejects a delivery only before the inbox stores it: while the consumer's circuit breaker is open,
    /// or when the receive stage fails. Those rejections count toward the limit, and a message that reaches it is
    /// dropped without Headless ever storing it; JetStream publishes a <c>MAX_DELIVERIES</c> advisory for it. A stored
    /// message retries from storage and is not redelivered by JetStream. A rejected delivery is redelivered after a
    /// delay that doubles from about one second to 30 seconds, so a limit of 10 survives roughly three minutes of an
    /// open circuit.
    /// </remarks>
    /// <param name="maxDeliver">The most deliveries of one message.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDeliver"/> is not positive.</exception>
    public NatsConsumerConfigBuilder MaxDeliver(int maxDeliver)
    {
        _maxDeliver = Argument.IsPositive(maxDeliver);
        return this;
    }

    /// <summary>
    /// Lets JetStream delete this consumer's durables after they have had no pulling client for
    /// <paramref name="inactiveThreshold"/>, so the durables of a retired consumer identity do not pile up. Off by
    /// default: a durable lives until it is deleted.
    /// </summary>
    /// <remarks>
    /// A Bus durable starts at the messages published after it is created (<c>DeliverPolicy.New</c>). Once JetStream
    /// deletes it during a downtime longer than the threshold, the next start creates a new durable, and every Bus
    /// message published while the consumer was down is lost to it. Set the threshold well above any outage or deploy
    /// the consumer must survive. A Queue durable starts at the first stored message, so a deleted one loses nothing
    /// the stream still holds.
    /// </remarks>
    /// <param name="inactiveThreshold">How long a durable may have no pulling client.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="inactiveThreshold"/> is not positive.</exception>
    public NatsConsumerConfigBuilder InactiveThreshold(TimeSpan inactiveThreshold)
    {
        _inactiveThreshold = Argument.IsPositive(inactiveThreshold);
        return this;
    }

    internal NatsConsumerConfig Build()
    {
        return new(_isSharded, _ackWait, _maxAckPending, _maxDeliver, _inactiveThreshold);
    }
}

internal sealed record NatsConsumerConfig(
    bool IsSharded,
    TimeSpan? AckWait = null,
    int? MaxAckPending = null,
    int? MaxDeliver = null,
    TimeSpan? InactiveThreshold = null
)
{
    /// <summary>Writes the limits this consumer sets onto <paramref name="config"/>, leaving the rest as they are.</summary>
    public void ApplyTo(ConsumerConfig config)
    {
        if (AckWait is { } ackWait)
        {
            config.AckWait = ackWait;
        }

        if (MaxAckPending is { } maxAckPending)
        {
            config.MaxAckPending = maxAckPending;
        }

        if (MaxDeliver is { } maxDeliver)
        {
            config.MaxDeliver = maxDeliver;
        }

        if (InactiveThreshold is { } inactiveThreshold)
        {
            config.InactiveThreshold = inactiveThreshold;
        }
    }
}
