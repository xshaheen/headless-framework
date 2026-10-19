// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection("AzureServiceBusEmulator")]
public sealed class AzureServiceBusEmulatorBrokerFaultTests(AzureServiceBusEmulatorFixture fixture)
    : BrokerFaultTestsBase
{
    protected override string ProviderName => AzureServiceBusEmulatorFixture.ProviderName;

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateQueueSessionAsync(cancellationToken);
    }

    // The restart drops every message and recreates the Config.json entities, so delivery resumes on the same queue
    // without the provider provisioning anything.
    protected override ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateInterruptibleSessionAsync(cancellationToken);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause()
    {
        return base.should_resume_delivery_once_after_consumer_pause();
    }

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart()
    {
        return base.should_resume_delivery_once_after_broker_restart();
    }
}
