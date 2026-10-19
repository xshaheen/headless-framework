// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Capabilities;
using Tests.Simulation;

namespace Tests.Contracts;

/// <summary>
/// One simulated broker for one test, and what the shared contract suites need to run against it: sessions whose
/// waits run on simulated time, the provider driver, and a network link that drops the broker's connection on cue.
/// </summary>
internal sealed class SimulatorConformance : IAsyncDisposable
{
    public SimulatorConformance()
    {
        Broker = new SimulatedBroker(new SimulationClock());
        WaitClock = new SimulationWaitClock(Broker);
        Driver = new SimulatorProviderConformanceDriver(this);
    }

    public static TransportConformanceProfile Profile => TransportConformanceManifest.Simulator;

    public SimulatedBroker Broker { get; }

    public SimulationWaitClock WaitClock { get; }

    public TransportProviderConformanceDriver Driver { get; }

    /// <summary>Opens a session on a queue of its own, as a broker leaf's per-test destination.</summary>
    public ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(
        CancellationToken cancellationToken,
        byte concurrency = 1
    )
    {
        var destination = $"conformance-{Guid.NewGuid():N}";
        return CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Queue, destination, destination, "replica-1"),
            cancellationToken,
            concurrency
        );
    }

    public async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken,
        byte concurrency = 1
    )
    {
#pragma warning disable CA2000 // Ownership transfers to the returned conformance session.
        var producer = Broker.CreateTransport(endpoint.Lane);
        var consumer = Broker.CreateConsumerClient(endpoint.ToRequest(concurrency));
#pragma warning restore CA2000
        await consumer.SubscribeAsync([endpoint.LogicalName], cancellationToken);

        return new TransportConsumerConformanceSession(
            endpoint.LogicalName,
            producer,
            consumer,
            TimeSpan.FromSeconds(1),
            createReplacementSession: token => CreateSessionAsync(endpoint, token, concurrency),
            timeProvider: WaitClock
        );
    }

    /// <summary>
    /// A link whose cut drops the broker's connection and holds it down until the session's client has seen a receive
    /// fail, so the client must reconnect even when the link comes back at once, as a reset socket forces it to.
    /// </summary>
    public INetworkLink CreateLink(TransportConsumerConformanceSession session)
    {
        return new SimulatedNetworkLink(Broker, ((SimulatedConsumerClient)session.Consumer).Subscription);
    }

    public ValueTask DisposeAsync() => WaitClock.DisposeAsync();

    private sealed class SimulatedNetworkLink(SimulatedBroker broker, string subscription) : INetworkLink
    {
        public async Task CutAsync(CancellationToken cancellationToken = default)
        {
            var failuresBefore = broker.Events.Count(_IsReceiveFailure);
            broker.Disconnect();
            await broker.WaitForEventsAsync(_IsReceiveFailure, failuresBefore + 1, cancellationToken);
        }

        public Task RestoreAsync(CancellationToken cancellationToken = default)
        {
            broker.Restore();
            return Task.CompletedTask;
        }

        private bool _IsReceiveFailure(BrokerEvent brokerEvent) =>
            brokerEvent.Kind is BrokerEventKind.ReceiveFailed
            && string.Equals(brokerEvent.Subscription, subscription, StringComparison.Ordinal);
    }

    private sealed class SimulatorProviderConformanceDriver(SimulatorConformance simulator)
        : TransportProviderConformanceDriver
    {
        public override string ProviderName => Profile.Provider;

        public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => Profile.MalformedEnvelopeBound!;

        public override bool SupportsEveryInstance => true;

        public override bool SupportsRequestReply => true;

        public override void ConfigureRequestReplyTransport(MessagingSetupBuilder setup)
        {
            setup.UseSimulatedBroker(simulator.Broker);
        }

        public override ValueTask<bool> HasReplyObjectsAsync(string replyAddress, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(simulator.Broker.HasReplyListener(replyAddress));
        }

        public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
            TransportConformanceEndpoint endpoint,
            CancellationToken cancellationToken
        )
        {
            return simulator.CreateSessionAsync(endpoint, cancellationToken);
        }
    }
}
