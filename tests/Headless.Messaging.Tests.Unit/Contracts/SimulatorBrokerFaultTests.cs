// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Tests.Simulation;

namespace Tests.Contracts;

/// <summary>The shared pause and broker-restart recovery scenarios, run against the simulated broker.</summary>
public sealed class SimulatorBrokerFaultTests : BrokerFaultTestsBase
{
    private readonly SimulatorConformance _simulator = new();

    protected override string ProviderName => SimulatedBroker.ProviderName;

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    ) => _simulator.CreateQueueSessionAsync(cancellationToken);

    /// <summary>
    /// A restart drops every connection while the broker's queues survive, so it is the simulator's connection loss,
    /// held until the client has seen it, then its return.
    /// </summary>
    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        var session = await _simulator.CreateQueueSessionAsync(cancellationToken);
        var link = _simulator.CreateLink(session);

        return new InterruptibleTransportSession(
            session,
            async token =>
            {
                await link.CutAsync(token);
                await link.RestoreAsync(token);
            },
            () => ValueTask.CompletedTask
        );
    }

    [Fact]
    public override Task should_resume_delivery_once_after_consumer_pause() =>
        base.should_resume_delivery_once_after_consumer_pause();

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart() =>
        base.should_resume_delivery_once_after_broker_restart();

    protected override async ValueTask DisposeAsyncCore()
    {
        await _simulator.DisposeAsync();
        await base.DisposeAsyncCore();
    }
}
