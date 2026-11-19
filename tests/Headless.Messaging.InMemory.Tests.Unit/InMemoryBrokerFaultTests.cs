// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

/// <summary>
/// The shared pause and broker-restart recovery scenarios against the in-process broker. The broker is the host process
/// itself, so its restart cell is not applicable and that scenario skips.
/// </summary>
public sealed class InMemoryBrokerFaultTests : BrokerFaultTestsBase
{
    private readonly InMemoryProviderConformanceTests.InMemoryProviderConformanceDriver _driver =
        InMemoryProviderConformanceTests.CreateDriver();

    protected override string ProviderName => _driver.ProviderName;

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        var destination = $"conformance-{Guid.NewGuid():N}";

        return _driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Queue, destination, destination, "replica-1"),
            cancellationToken
        );
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause() =>
        base.should_resume_delivery_once_after_consumer_pause();

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart() =>
        base.should_resume_delivery_once_after_broker_restart();
}
