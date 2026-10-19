// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection("AzureServiceBus")]
public sealed class AzureServiceBusBrokerFaultTests(AzureServiceBusFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "Azure Service Bus";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    // No interruptible session: the suite runs against a managed Azure namespace, which a test cannot restart, so the
    // manifest leaves the scenario unsupported and this skips with the manifest's reason.
    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart()
    {
        return base.should_resume_delivery_once_after_broker_restart();
    }
}
