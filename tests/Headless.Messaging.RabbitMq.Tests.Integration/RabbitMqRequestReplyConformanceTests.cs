// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.RequestReply;

namespace Tests;

/// <summary>The shared request/reply suite against a real RabbitMQ broker.</summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqRequestReplyConformanceTests(RabbitMqFixture fixture)
    : TransportRequestReplyConformanceTestsBase
{
    protected override ValueTask<TransportProviderConformanceDriver> CreateDriverAsync()
    {
        return ValueTask.FromResult<TransportProviderConformanceDriver>(new RabbitMqProviderConformanceDriver(fixture));
    }
}
