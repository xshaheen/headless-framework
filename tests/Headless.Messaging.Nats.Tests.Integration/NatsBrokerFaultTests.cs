// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection("Nats")]
public sealed class NatsBrokerFaultTests(NatsFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "NATS";

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
