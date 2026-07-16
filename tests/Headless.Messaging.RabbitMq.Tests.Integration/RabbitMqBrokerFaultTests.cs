// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqBrokerFaultTests(RabbitMqFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "RabbitMQ";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }
}
