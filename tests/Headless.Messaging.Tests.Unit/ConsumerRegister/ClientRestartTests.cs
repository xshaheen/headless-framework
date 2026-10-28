// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests;

/// <summary>
/// The consumer register's transport check, on the real pipeline against the simulated broker: a client whose channel
/// shut down is restarted at the next check, whatever the other clients of its subscription report meanwhile, and only
/// the subscriptions holding such a client are rebuilt.
/// </summary>
public sealed class ClientRestartTests : TestBase
{
    // The transport check runs every 30 seconds; a little more than one interval covers the next check.
    private static readonly TimeSpan _PastTransportCheck = TimeSpan.FromSeconds(31);
    private static readonly TimeSpan _OpenDuration = TimeSpan.FromMinutes(2);

    private const string _AuditMessage = "tests.restart.audit";

    [Fact]
    [Guarantee("MSG-CONN-03")]
    public async Task should_restart_a_client_whose_channel_shut_down_when_another_client_of_its_subscription_registers()
    {
        MessagingGuarantees.RequireSupport("MSG-CONN-03");

        // given: two clients on one subscription
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup => setup.Options.ConsumerThreadCount = 2
        );
        var subscription = host
            .Broker.SubscriptionsFor(MessageLane.Queue, PipelineOrder.Name)
            .Should()
            .ContainSingle()
            .Subject;
        var clients = host.Broker.ClientsOf(subscription);
        clients.Should().HaveCount(2);

        // when: one client's channel shuts down, then the other reports its consumer registered
        clients[0].CloseChannel();
        clients[1].ReportRegistered();
        await host.RunForAsync(_PastTransportCheck, AbortToken);

        // then: the subscription is back to two open clients, so the dead one was restarted
        await host.WaitUntilAsync(() => !host.Broker.ClientsOf(subscription).Intersect(clients).Any(), AbortToken);
        await _WhenRestartFinishedAsync(host);
        host.Broker.ClientsOf(subscription).Should().HaveCount(2);
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
    }

    [Fact]
    public async Task should_rebuild_only_the_subscription_whose_client_was_cancelled_at_the_next_transport_check()
    {
        // given
        var probe = new PipelineProbe();
        await using var host = await LedgerHosts.StartAsync(probe, AbortToken);
        var cancelled = _OnlyClient(host, host.OrderSubscription());
        var healthy = _OnlyClient(host, host.RefundSubscription());

        // when
        cancelled.Report(MqLogType.ConsumerCancelled);
        await host.RunForAsync(_PastTransportCheck, AbortToken);

        // then: the cancelled subscription has a new client that delivers, and the other kept its client
        await host.WaitUntilAsync(
            () => !host.Broker.ClientsOf(host.OrderSubscription()).Contains(cancelled),
            AbortToken
        );
        await _WhenRestartFinishedAsync(host);
        _OnlyClient(host, host.OrderSubscription()).Should().NotBeSameAs(cancelled);
        _OnlyClient(host, host.RefundSubscription()).Should().BeSameAs(healthy);
        host.Register().IsHealthy().Should().BeTrue();

        await host.EnqueueAsync("order-1", AbortToken);
        await host.EnqueueRefundAsync("refund-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("refund-1", 1), AbortToken);
    }

    [Fact]
    public async Task should_stop_the_replacement_when_final_shutdown_starts_while_a_subscription_restart_creates_it()
    {
        // given: the restart of a degraded subscription is creating its replacement client
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = host.OrderSubscription();
        var refund = host.RefundSubscription();
        _OnlyClient(host, order).CloseChannel();
        var hold = host.ClientFactory().Hold(order, skip: 1);
        var restart = host.Register().ReStartAsync(cancellationToken: AbortToken).AsTask();
        await host.WithinBoundAsync(hold.Entered, AbortToken);

        // when
        var stop = host.StopAsync(AbortToken);
        await host.WithinBoundAsync(restart, AbortToken);
        await host.WithinBoundAsync(stop, AbortToken);

        // then: shutdown canceled the replacement, and no client of either subscription is left open
        hold.WasCanceled.Should().BeTrue();
        host.Broker.ClientsOf(order).Should().BeEmpty();
        host.Broker.ClientsOf(refund).Should().BeEmpty();
    }

    [Fact]
    public async Task should_apply_a_topology_change_requested_during_a_subscription_restart_once_it_finishes()
    {
        // given: the restart of a degraded subscription is creating its replacement client
        var probe = new PipelineProbe();
        await using var host = await LedgerHosts.StartAsync(probe, AbortToken);
        var order = host.OrderSubscription();
        _OnlyClient(host, order).CloseChannel();
        var hold = host.ClientFactory().Hold(order, skip: 1);
        var restart = host.Register().ReStartAsync(cancellationToken: AbortToken).AsTask();
        await host.WithinBoundAsync(hold.Entered, AbortToken);

        // when: a runtime subscription attaches meanwhile, then the restart finishes
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscribe = _SubscribeAuditAsync(host, received);
        hold.Release();
        await host.WithinBoundAsync(restart, AbortToken);
        await using var attached = await subscribe.WaitAsync(PipelineHosts.Bound, AbortToken);

        // then: both the rebuilt subscription and the attached one consume
        await _PublishAuditAsync(host, "audit-1");
        (await received.Task.WaitAsync(PipelineHosts.Bound, AbortToken)).Should().Be("audit-1");
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
    }

    [Fact]
    public async Task should_start_the_replacement_paused_and_keep_a_subscription_sharing_its_open_circuit_paused()
    {
        // given: the ledger's circuit is open, holding the clients of both of its subscriptions
        var probe = new PipelineProbe { Behavior = _FailFirstOrder };
        await using var host = await LedgerHosts.StartAsync(probe, AbortToken, _OpenOnFirstFailure);
        var order = host.OrderSubscription();
        var refund = host.RefundSubscription();
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WaitUntilAsync(
            () =>
                host.Broker.ClientsOf(order).All(static c => c.IsPaused)
                && host.Broker.ClientsOf(refund).All(static c => c.IsPaused),
            AbortToken
        );
        host.LedgerCircuit().State.Should().Be(CircuitBreakerState.Open);
        var shutDown = _OnlyClient(host, order);
        var survivor = _OnlyClient(host, refund);

        // when
        shutDown.CloseChannel();
        await host.Register().ReStartAsync(cancellationToken: AbortToken);

        // then: the replacement is held by the open circuit, and the subscription that kept its client stays held
        var replacement = _OnlyClient(host, order);
        replacement.Should().NotBeSameAs(shutDown);
        replacement.IsPaused.Should().BeTrue();
        _OnlyClient(host, refund).Should().BeSameAs(survivor);
        survivor.IsPaused.Should().BeTrue();

        // and: once the circuit half-opens both take deliveries again
        await host.RunForAsync(_OpenDuration + PipelineHosts.PollInterval, AbortToken);
        await host.EnqueueRefundAsync("refund-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("refund-1", 1), AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
    }

    [Fact]
    public async Task should_retire_and_not_recreate_a_degraded_subscription_whose_group_was_removed()
    {
        // given: a runtime subscription whose client lost its channel
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var attached = await _SubscribeAuditAsync(host, received);
        var audit = host.Broker.SubscriptionsFor(MessageLane.Bus, _AuditMessage).Should().ContainSingle().Subject;
        var auditClient = _OnlyClient(host, audit);
        var order = _OnlyClient(host, host.OrderSubscription());
        auditClient.Report(MqLogType.ConsumerShutdown);

        // and: it was removed from the runtime registry, the way the runtime subscriber does before it reports the
        // change, so the restart sees a desired set without it
        host.GetRequiredService<IRuntimeConsumerRegistry>().Unregister(attached.SubscriptionId!).Should().BeTrue();
        host.GetRequiredService<MethodMatcherCache>().Invalidate();
        var created = host.ClientFactory().CreatedFor(audit);

        // when
        await host.Register().ReStartAsync(cancellationToken: AbortToken);

        // then
        host.Broker.ClientsOf(audit).Should().BeEmpty();
        host.ClientFactory().CreatedFor(audit).Should().Be(created);
        _OnlyClient(host, host.OrderSubscription()).Should().BeSameAs(order);
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_rebuild_every_subscription_after_the_broker_refused_a_subscription_restart()
    {
        // given: the restart of a degraded subscription found the broker down, so it has no client
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = host.OrderSubscription();
        var refund = host.RefundSubscription();
        var survivor = _OnlyClient(host, refund);
        _OnlyClient(host, order).CloseChannel();
        host.Broker.Disconnect();
        await host.Register().ReStartAsync(cancellationToken: AbortToken);
        host.Broker.ClientsOf(order).Should().BeEmpty();
        _OnlyClient(host, refund).Should().BeSameAs(survivor);
        host.Register().IsHealthy().Should().BeFalse();

        // when
        host.Broker.Restore();
        await host.Register().ReStartAsync(cancellationToken: AbortToken);

        // then: the failure belonged to no subscription, so every subscription was rebuilt
        host.Broker.ClientsOf(order).Should().ContainSingle();
        _OnlyClient(host, refund).Should().NotBeSameAs(survivor);
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_rebuild_every_subscription_on_a_forced_restart_when_only_one_is_degraded()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = _OnlyClient(host, host.OrderSubscription());
        var refund = _OnlyClient(host, host.RefundSubscription());
        order.CloseChannel();

        // when
        await host.Register().ReStartAsync(force: true, AbortToken);

        // then
        _OnlyClient(host, host.OrderSubscription()).Should().NotBeSameAs(order);
        _OnlyClient(host, host.RefundSubscription()).Should().NotBeSameAs(refund);
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_retry_only_the_subscription_whose_replacement_failed_to_start()
    {
        // given: the replacement of a degraded subscription failed to start
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = host.OrderSubscription();
        var survivor = _OnlyClient(host, host.RefundSubscription());
        _OnlyClient(host, order).CloseChannel();
        host.ClientFactory().Fail(order, skip: 0, new InvalidOperationException("The client factory failed."));

        var failed = async () => await host.Register().ReStartAsync(cancellationToken: AbortToken);
        await failed.Should().ThrowAsync<InvalidOperationException>();
        host.Broker.ClientsOf(order).Should().BeEmpty();
        host.Register().IsHealthy().Should().BeFalse();

        // when
        await host.Register().ReStartAsync(cancellationToken: AbortToken);

        // then: the failed subscription has a client again, and the other kept its own
        host.Broker.ClientsOf(order).Should().ContainSingle();
        _OnlyClient(host, host.RefundSubscription()).Should().BeSameAs(survivor);
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_rebuild_every_subscription_after_a_full_restart_failed_to_connect_a_client()
    {
        // given: a forced rebuild whose replacement client could not reach the broker
        var probe = new PipelineProbe();
        await using var host = await LedgerHosts.StartAsync(probe, AbortToken);
        var order = host.OrderSubscription();
        host.ClientFactory()
            .Fail(order, skip: 1, new BrokerConnectionException(new InvalidOperationException("The broker is down.")));

        var failed = async () => await host.Register().ReStartAsync(force: true, AbortToken);
        await failed.Should().ThrowAsync<BrokerConnectionException>();
        host.Register().IsHealthy().Should().BeFalse();

        // when
        await host.Register().ReStartAsync(cancellationToken: AbortToken);

        // then
        host.Broker.ClientsOf(order).Should().ContainSingle();
        host.Broker.ClientsOf(host.RefundSubscription()).Should().ContainSingle();
        host.Register().IsHealthy().Should().BeTrue();
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WithinBoundAsync(probe.WhenCompletedAsync("order-1", 1), AbortToken);
    }

    // A restart holds the restart gate from retiring the old clients until its replacements receive, and a topology
    // refresh waits for that gate, so once the old clients have closed the refresh returns only after the restart
    // finished. A client joins its group without a broker event, so no broker wait can tell when it is listed.
    private Task _WhenRestartFinishedAsync(SimulationHost host) =>
        host.WithinBoundAsync(host.Register().OnTopologyChangedAsync(AbortToken).AsTask(), AbortToken);

    private static SimulatedConsumerClient _OnlyClient(SimulationHost host, string subscription) =>
        host.Broker.ClientsOf(subscription).Should().ContainSingle().Subject;

    private Task<RuntimeSubscriptionHandle> _SubscribeAuditAsync(
        SimulationHost host,
        TaskCompletionSource<string> received
    )
    {
        return host.GetRequiredService<IRuntimeSubscriber>()
            .SubscribeAsync<PipelineRefund>(
                (context, _, _) =>
                {
                    received.TrySetResult(context.Message.Id);
                    return ValueTask.CompletedTask;
                },
                new RuntimeSubscriptionOptions
                {
                    MessageName = _AuditMessage,
                    Identity = _AuditMessage,
                    HandlerId = _AuditMessage,
                },
                AbortToken
            )
            .AsTask();
    }

    private async Task _PublishAuditAsync(SimulationHost host, string id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IBus>()
            .PublishAsync(new PipelineRefund(id), new PublishOptions { MessageName = _AuditMessage }, AbortToken);
    }

    private static void _OpenOnFirstFailure(MessagingSetupBuilder setup)
    {
        setup.Options.CircuitBreaker.FailureThreshold = 1;
        setup.Options.CircuitBreaker.OpenDuration = _OpenDuration;
        setup.Options.CircuitBreaker.MaxOpenDuration = _OpenDuration;
    }

    private static ValueTask _FailFirstOrder(string id, int attempt, CancellationToken cancellationToken) =>
        string.Equals(id, "order-1", StringComparison.Ordinal) && attempt == 1
            ? throw new TimeoutException("The dependency timed out.")
            : ValueTask.CompletedTask;
}
