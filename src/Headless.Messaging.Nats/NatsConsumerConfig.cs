// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Nats;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Fluent builder for NATS JetStream consumer options applied to a consumer registration.</summary>
[PublicAPI]
public sealed class NatsConsumerConfigBuilder
{
    private bool _isSharded;

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

    internal NatsConsumerConfig Build()
    {
        return new(_isSharded);
    }
}

internal sealed record NatsConsumerConfig(bool IsSharded);
