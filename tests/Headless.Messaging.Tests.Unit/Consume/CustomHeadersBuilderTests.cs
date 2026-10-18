// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Consume;

/// <summary>
/// A custom headers builder that fails on a transient error, such as a lookup service being briefly down: the delivery
/// must go back to the broker to be tried again, and only the broker's own delivery limit dead-letters it. The
/// simulated client settles a builder failure as Amazon SQS, Redis, and Pulsar do today, as a malformed envelope.
/// </summary>
public sealed class CustomHeadersBuilderTests : TestBase
{
    private const int _MaxDeliveries = 3;

    [Fact]
    [Guarantee("MSG-SETTLE-08")]
    public async Task should_redeliver_then_dead_letter_after_the_delivery_limit_when_the_headers_builder_fails_transiently()
    {
        MessagingGuarantees.RequireSupport("MSG-SETTLE-08");

        // given: a builder whose dependency keeps timing out, on a broker that dead-letters after three deliveries
        var broker = new SimulatedBroker(new SimulationClock())
        {
            MaxDeliveries = _MaxDeliveries,
            CustomHeadersBuilder = static _ => throw new TimeoutException("The header lookup service timed out."),
        };
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            PipelineHosts.QuietRetryPickup,
            broker
        );

        // when
        await host.EnqueueAsync("order-1", AbortToken);
        var firstSettlement = host.Broker.WaitForEventAsync(
            static e => e.Kind is BrokerEventKind.Committed or BrokerEventKind.Rejected,
            AbortToken
        );
        await host.WithinBoundAsync(firstSettlement, AbortToken);

        // then: the first delivery goes back instead of being dropped
        (await firstSettlement)
            .Kind.Should()
            .Be(BrokerEventKind.Rejected);

        // and: the broker dead-letters it once its delivery limit is spent, and nothing ever commits or handles it
        await host.StepShortWaitsUntilAsync(
            host.Broker.WaitForEventAsync(static e => e.Kind == BrokerEventKind.DeadLettered, AbortToken),
            AbortToken
        );
        host.Broker.Events.Should()
            .ContainSingle(e => e.Kind == BrokerEventKind.DeadLettered)
            .Which.DeliveryCount.Should()
            .Be(_MaxDeliveries);
        host.Broker.Events.Should().NotContain(e => e.Kind == BrokerEventKind.Committed);
        probe.Attempts("order-1").Should().Be(0);
    }
}
