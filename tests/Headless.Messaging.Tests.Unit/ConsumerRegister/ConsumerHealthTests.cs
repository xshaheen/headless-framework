// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Tests.Simulation;

namespace Tests;

/// <summary>
/// The consumer register's health, on the real pipeline against the simulated broker: each client's health events
/// mark only that client, so no client's report hides another client's failure.
/// </summary>
public sealed class ConsumerHealthTests : TestBase
{
    private static readonly TimeSpan _OpenDuration = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task should_report_unhealthy_when_a_client_shut_down_and_another_client_of_its_subscription_registers()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken, _TwoClients);
        var clients = host.Broker.ClientsOf(host.OrderSubscription());
        clients.Should().HaveCount(2);

        // when
        clients[0].CloseChannel();
        clients[1].ReportRegistered();

        // then
        host.Register().IsHealthy().Should().BeFalse();
    }

    [Fact]
    public async Task should_report_unhealthy_when_a_client_is_cancelled_and_a_client_of_another_subscription_registers()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Subject;
        var refund = host.Broker.ClientsOf(host.RefundSubscription()).Should().ContainSingle().Subject;

        // when
        order.Report(MqLogType.ConsumerCancelled);
        refund.Report(MqLogType.ConsumerRegistered);

        // then
        host.Register().IsHealthy().Should().BeFalse();
    }

    [Fact]
    public async Task should_report_healthy_again_when_the_degraded_client_itself_registers()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Subject;
        order.Report(MqLogType.ConsumerCancelled);
        host.Register().IsHealthy().Should().BeFalse();

        // when
        order.Report(MqLogType.ConsumerRegistered);

        // then
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_a_healthy_register_and_its_clients_when_a_client_reports_a_redis_consume_error()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var before = host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Subject;

        // when
        before.Report(MqLogType.RedisConsumeError);
        await host.Register().ReStartAsync(cancellationToken: AbortToken);

        // then: the register stays healthy, so the transport check's restart leaves the client alone
        host.Register().IsHealthy().Should().BeTrue();
        host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Which.Should().BeSameAs(before);
    }

    [Fact]
    public async Task should_stay_unhealthy_when_a_client_reports_a_redis_consume_error_while_another_is_degraded()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Which.CloseChannel();

        // when
        host.Broker.ClientsOf(host.RefundSubscription())
            .Should()
            .ContainSingle()
            .Which.Report(MqLogType.RedisConsumeError);

        // then
        host.Register().IsHealthy().Should().BeFalse();
    }

    [Fact]
    public async Task should_ignore_a_consumer_cancelled_from_a_client_of_a_subscription_its_circuit_paused()
    {
        // given: the ledger's circuit is open, so both subscriptions' clients are paused
        var probe = new PipelineProbe { Behavior = _FailFirstOrder };
        await using var host = await LedgerHosts.StartAsync(probe, AbortToken, _OpenOnFirstFailure);
        await host.EnqueueAsync("order-1", AbortToken);
        await host.WaitUntilAsync(
            () => host.Broker.ClientsOf(host.OrderSubscription()).All(static c => c.IsPaused),
            AbortToken
        );
        host.LedgerCircuit().State.Should().Be(CircuitBreakerState.Open);

        // when: the paused client reports the cancel a transport raises for its own pause
        host.Broker.ClientsOf(host.OrderSubscription())
            .Should()
            .ContainSingle()
            .Which.Report(MqLogType.ConsumerCancelled);

        // then
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_ignore_a_late_shutdown_from_a_client_a_restart_retired()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var retired = host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Subject;
        await host.Register().ReStartAsync(force: true, AbortToken);
        host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Which.Should().NotBeSameAs(retired);

        // when
        retired.Report(MqLogType.ConsumerShutdown);

        // then
        host.Register().IsHealthy().Should().BeTrue();
    }

    [Fact]
    public async Task should_report_unhealthy_when_the_broker_refuses_a_subscription_before_it_has_clients()
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);

        // when: a rebuild finds the broker down, so no subscription gets a client
        host.Broker.Disconnect();
        await host.Register().ReStartAsync(force: true, AbortToken);

        // then
        host.Broker.ClientsOf(host.OrderSubscription()).Should().BeEmpty();
        host.Register().IsHealthy().Should().BeFalse();
    }

    private static void _TwoClients(MessagingSetupBuilder setup) => setup.Options.ConsumerThreadCount = 2;

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
