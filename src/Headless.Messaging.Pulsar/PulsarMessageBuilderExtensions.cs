// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Pulsar.Client.Api;
using Pulsar.Client.Common;

namespace Headless.Messaging;

/// <summary>Extension methods that attach Pulsar provider-specific options to a tuned consumer.</summary>
[PublicAPI]
public static class PulsarMessageBuilderExtensions
{
    /// <summary>
    /// Configures Pulsar consumer options for the declared consumer a <c>Tune</c> call targets.
    /// </summary>
    /// <param name="builder">The consumer tuning builder.</param>
    /// <param name="configure">A delegate that configures the Pulsar consumer options.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static ConsumerTuningBuilder UsePulsar(
        this ConsumerTuningBuilder builder,
        Action<PulsarConsumerConfigBuilder> configure
    )
    {
        _SetConsumerConfig(builder, configure);
        return builder;
    }

    private static void _SetConsumerConfig(
        IConsumerProviderConfigBuilder builder,
        Action<PulsarConsumerConfigBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var configBuilder = new PulsarConsumerConfigBuilder();
        configure(configBuilder);
        builder.SetConsumerProviderConfig(configBuilder.Build());
    }
}

/// <summary>Fluent builder for Pulsar consumer options applied to a tuned consumer.</summary>
/// <remarks>
/// Each <c>UsePulsar(...)</c> call replaces the Pulsar settings of an earlier one for the same consumer, so set every
/// Pulsar option of a consumer in one call. Every consumer that attaches to one subscription must set the same options:
/// the Queue lane subscription (<c>headless-queue</c>) is shared by every consumer of the queue message, and the broker
/// rejects a consumer whose subscription type differs from the one already attached.
/// </remarks>
[PublicAPI]
public sealed class PulsarConsumerConfigBuilder
{
    /// <summary>The shortest ack timeout Pulsar.Client accepts.</summary>
    internal static readonly TimeSpan MinAckTimeout = TimeSpan.FromSeconds(1);

    private bool _keyShared;
    private int? _maxRedeliveryCount;
    private string? _deadLetterTopic;
    private TimeSpan? _ackTimeout;

    /// <summary>
    /// Subscribes with a <c>Key_Shared</c> subscription instead of <c>Shared</c>: the broker sends every message with
    /// one key to the same consumer, in publish order. The key is the publish-side <c>RoutingAffinityKey</c>.
    /// </summary>
    /// <remarks>
    /// Per-key order holds up to the consumer client only. Handlers run in key order only with <c>Concurrency(1)</c>
    /// and a single dispatcher thread. An every-instance consumer subscribes exclusively, so <c>KeyShared()</c> on one
    /// fails its creation with <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <returns>The same builder for chaining.</returns>
    public PulsarConsumerConfigBuilder KeyShared()
    {
        _keyShared = true;
        return this;
    }

    /// <summary>
    /// Moves a message to a dead-letter topic once the broker has redelivered it <paramref name="maxRedeliveryCount"/>
    /// times. Off by default: a rejected message is redelivered forever.
    /// </summary>
    /// <remarks>
    /// The consumer rejects a message only when the messaging core cannot admit it, for example while storage is down
    /// or the consumer's circuit breaker is open. A handler failure retries from storage and never reaches the limit.
    /// A dead-lettered message was never stored, and nothing in the framework consumes the dead-letter topic. Without
    /// <see cref="AckTimeout"/>, Pulsar.Client applies a 30-second ack timeout whenever a dead-letter policy is set.
    /// </remarks>
    /// <param name="maxRedeliveryCount">The most redeliveries before the message moves to the dead-letter topic.</param>
    /// <param name="deadLetterTopic">
    /// The dead-letter topic. Defaults to <c>{topic}-{subscription}-DLQ</c> for each subscribed topic.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRedeliveryCount"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="deadLetterTopic"/> is empty or whitespace.</exception>
    public PulsarConsumerConfigBuilder DeadLetter(int maxRedeliveryCount, string? deadLetterTopic = null)
    {
        _maxRedeliveryCount = Argument.IsPositive(maxRedeliveryCount);
        _deadLetterTopic = deadLetterTopic is null ? null : Argument.IsNotNullOrWhiteSpace(deadLetterTopic);
        return this;
    }

    /// <summary>
    /// Sets how long a received message may stay unacknowledged before Pulsar.Client asks the broker to redeliver it.
    /// Off by default, unless <see cref="DeadLetter"/> is set.
    /// </summary>
    /// <remarks>
    /// The consumer acknowledges a message once the messaging core has stored or skipped it, before the handler runs,
    /// so the timeout bounds admission, not handler time.
    /// </remarks>
    /// <param name="ackTimeout">The acknowledgement timeout; at least one second.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ackTimeout"/> is under one second.</exception>
    public PulsarConsumerConfigBuilder AckTimeout(TimeSpan ackTimeout)
    {
        _ackTimeout = Argument.IsGreaterThanOrEqualTo(ackTimeout, MinAckTimeout);
        return this;
    }

    internal PulsarConsumerConfig Build()
    {
        return new(_keyShared, _maxRedeliveryCount, _deadLetterTopic, _ackTimeout);
    }
}

internal sealed record PulsarConsumerConfig(
    bool KeyShared,
    int? MaxRedeliveryCount = null,
    string? DeadLetterTopic = null,
    TimeSpan? AckTimeout = null
)
{
    /// <summary>Writes the options this consumer sets onto <paramref name="builder"/>, leaving the rest as they are.</summary>
    public ConsumerBuilder<byte[]> ApplyTo(ConsumerBuilder<byte[]> builder)
    {
        if (KeyShared)
        {
            // Auto-split hands each attached consumer a share of the key hash range and rebalances as consumers come
            // and go, so replicas need no sticky range configuration.
            builder = builder
                .SubscriptionType(SubscriptionType.KeyShared)
                .KeySharedPolicy(KeySharedPolicy.KeySharedPolicyAutoSplit());
        }

        if (MaxRedeliveryCount is { } maxRedeliveryCount)
        {
            builder = builder.DeadLetterPolicy(new DeadLetterPolicy(maxRedeliveryCount, DeadLetterTopic));
        }

        if (AckTimeout is { } ackTimeout)
        {
            builder = builder.AckTimeout(ackTimeout);
        }

        return builder;
    }
}
