// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Confluent.Kafka;
using Headless.Checks;
using Headless.Messaging.Kafka;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

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

    internal KafkaConsumerConfig Build()
    {
        return new(_isolationLevel);
    }
}

internal sealed record KafkaConsumerConfig(IsolationLevel? IsolationLevel);
