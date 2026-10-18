// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.CircuitBreaker;

/// <summary>
/// The consumer circuit's hold on broker delivery, on the real pipeline against the simulated broker: an open circuit
/// pauses every client of its consumer, and a client the register adds while the circuit is open ends resumed once the
/// circuit half-opens.
/// </summary>
public sealed class CircuitPauseTests : TestBase
{
    private const string _OrderId = "order-1";
    private static readonly TimeSpan _OpenDuration = TimeSpan.FromMinutes(2);

    // The transport check runs every 30 seconds; a little more than one interval covers the next check.
    private static readonly TimeSpan _PastTransportCheck = TimeSpan.FromSeconds(31);

    [Fact]
    [Guarantee("MSG-CB-03")]
    public async Task should_pause_every_client_of_the_consumer_while_its_circuit_is_open_and_resume_them_when_it_half_opens()
    {
        // given: two clients, and a circuit that opens on the first transient failure
        var probe = new PipelineProbe { Behavior = _FailFirstAttempt };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, static setup => _Configure(setup, 2));
        var subscription = _Subscription(host);

        // when: the handler fails transiently
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WithinBoundAsync(probe.WhenAttemptsAsync(_OrderId, 1), AbortToken);

        // then: the circuit opens and holds both clients
        await host.WaitUntilAsync(() => host.Broker.ClientsOf(subscription).All(static c => c.IsPaused), AbortToken);
        host.ConsumerCircuit().State.Should().Be(CircuitBreakerState.Open);

        // and: once the open duration passes, the circuit half-opens and lets both take deliveries again
        await host.RunForAsync(_OpenDuration + PipelineHosts.PollInterval, AbortToken);
        await host.WaitUntilAsync(() => host.Broker.ClientsOf(subscription).All(static c => !c.IsPaused), AbortToken);
    }

    [Fact]
    [Guarantee("MSG-CB-03")]
    public async Task should_end_a_client_added_while_the_circuit_is_open_resumed_when_the_circuit_half_opens()
    {
        MessagingGuarantees.RequireSupport("MSG-CB-03");

        // given: an open circuit holding the subscription's one client
        var probe = new PipelineProbe { Behavior = _FailFirstAttempt };
        await using var host = await PipelineHosts.StartAsync(probe, AbortToken, static setup => _Configure(setup, 1));
        var subscription = _Subscription(host);
        await host.EnqueueAsync(_OrderId, AbortToken);
        await host.WaitUntilAsync(() => host.Broker.ClientsOf(subscription).All(static c => c.IsPaused), AbortToken);

        // and: the client's channel shuts down, so the next transport check rebuilds it, and the rebuilt client's pause
        // is held between being asked for and taking effect
        host.Broker.Faults.HoldPauses();
        host.Broker.ClientsOf(subscription).Should().ContainSingle().Which.CloseChannel();
        await host.RunForAsync(_PastTransportCheck, AbortToken);
        await host.WithinBoundAsync(host.Broker.Faults.WhenPauseHeldAsync(AbortToken), AbortToken);

        // when: the circuit half-opens while that pause is held, and the pause then takes effect
        var resumed = host.Broker.Events.Count(static e => e.Kind == BrokerEventKind.Resumed);
        await host.RunForAsync(_OpenDuration, AbortToken);
        await host.WaitUntilAsync(
            () => host.Broker.Events.Where(static e => e.Kind == BrokerEventKind.Resumed).Skip(resumed).Any(),
            AbortToken
        );
        host.Broker.Faults.ReleaseHeldPauses();

        // then: the rebuilt client ends resumed, as the half-open circuit expects
        await host.WaitUntilAsync(() => host.Broker.ClientsOf(subscription) is [{ IsPaused: false }], AbortToken);
    }

    private static void _Configure(MessagingSetupBuilder setup, int clients)
    {
        setup.Options.ConsumerThreadCount = clients;
        setup.Options.CircuitBreaker.FailureThreshold = 1;
        setup.Options.CircuitBreaker.OpenDuration = _OpenDuration;
        setup.Options.CircuitBreaker.MaxOpenDuration = _OpenDuration;
    }

    private static string _Subscription(SimulationHost host) =>
        host.Broker.SubscriptionsFor(MessageLane.Queue, PipelineOrder.Name).Should().ContainSingle().Subject;

    private static ValueTask _FailFirstAttempt(string id, int attempt, CancellationToken cancellationToken) =>
        attempt == 1 ? throw new TimeoutException("The dependency timed out.") : ValueTask.CompletedTask;
}
