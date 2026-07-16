// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;

namespace Tests;

/// <summary>Shared optional recovery scenarios for broker-backed transport leaves.</summary>
[PublicAPI]
public abstract class BrokerFaultTestsBase : TransportConsumerConformanceTestsBase
{
    public virtual async Task should_resume_delivery_once_after_consumer_pause()
    {
        RequireSupport(TransportConformanceScenario.ConsumerPauseRecovery);

        await using var session = await CreateSessionAsync(AbortToken);
        await session.StartAsync(cancellationToken: AbortToken);
        await session.Consumer.PauseAsync(AbortToken);

        var message = CreateFaultProbe(session.Destination);
        var result = await session.PublishAsync(message, AbortToken);
        result.Succeeded.Should().BeTrue();

        var stayedPaused = await session.RemainsEmptyAsync(TimeSpan.FromMilliseconds(750), AbortToken);
        stayedPaused.Should().BeTrue("a paused consumer must not dispatch new broker deliveries");

        await session.Consumer.ResumeAsync(AbortToken);
        var delivery = await session.ReceiveAsync(TimeSpan.FromSeconds(10), AbortToken);
        await session.Consumer.CommitAsync(delivery.SettlementValue, AbortToken);

        var receivedOnce = await session.RemainsEmptyAsync(session.NoRedeliveryWindow, AbortToken);
        receivedOnce.Should().BeTrue("resume must not register a duplicate callback or redeliver the committed probe");
    }

    protected abstract TransportMessage CreateFaultProbe(string destination);
}
