// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Simulation;

namespace Tests.Contracts;

/// <summary>
/// The shared connection-drop and publish-during-outage scenarios, run against the simulated broker with its scripted
/// connection loss as the link, and their reconnect windows passing in simulated time.
/// </summary>
public sealed class SimulatorNetworkFaultTests : NetworkFaultTestsBase
{
    private readonly SimulatorConformance _simulator = new();

    protected override string ProviderName => SimulatedBroker.ProviderName;

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    ) => _simulator.CreateQueueSessionAsync(cancellationToken);

    protected override async ValueTask<NetworkFaultSession> CreateNetworkFaultSessionAsync(
        CancellationToken cancellationToken
    )
    {
        var session = await _simulator.CreateQueueSessionAsync(cancellationToken);
        return new NetworkFaultSession(session, _simulator.CreateLink(session), () => ValueTask.CompletedTask);
    }

    [Fact]
    public override Task should_resume_delivery_once_after_the_connection_is_dropped() =>
        base.should_resume_delivery_once_after_the_connection_is_dropped();

    [Fact]
    public override Task should_keep_consuming_after_publishes_during_an_outage() =>
        base.should_keep_consuming_after_publishes_during_an_outage();

    protected override async ValueTask DisposeAsyncCore()
    {
        await _simulator.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
