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
    public async Task should_stop_a_client_whose_shutdown_waits_for_its_own_cancel_event_on_another_thread()
    {
        // given: a client that, like a RabbitMQ channel's synchronous Dispose, blocks its shutdown until the consumer
        // cancel it raises on the transport's dispatcher thread has been handled
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var clients = new List<DispatcherCancelOnShutdownClient>();
        host.ClientFactory()
            .WrapClients(inner =>
            {
                var client = new DispatcherCancelOnShutdownClient(inner);
                lock (clients)
                {
                    clients.Add(client);
                }

                return client;
            });
        await host.Register().ReStartAsync(force: true, AbortToken);

        // when: the host stops, retiring every subscription
        await host.DisposeAsync();

        // then: each subscription client's cancel event was handled while its shutdown waited on it (the probe clients
        // that only read message names are disposed, never shut down)
        var shutDown = clients.Where(static client => client.CancelHandledDuringShutdown is not null).ToArray();
        shutDown.Should().NotBeEmpty();
        shutDown.Should().OnlyContain(static client => client.CancelHandledDuringShutdown == true);
    }

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

    [Theory]
    [InlineData(MqLogType.ConsumerShutdown)]
    [InlineData(MqLogType.ServerConnError)]
    [InlineData(MqLogType.ConnectError)]
    public async Task should_report_unhealthy_when_a_client_loses_its_connection_and_another_subscription_registers(
        MqLogType connectionLoss
    )
    {
        // given
        await using var host = await LedgerHosts.StartAsync(new PipelineProbe(), AbortToken);
        var order = host.Broker.ClientsOf(host.OrderSubscription()).Should().ContainSingle().Subject;
        var refund = host.Broker.ClientsOf(host.RefundSubscription()).Should().ContainSingle().Subject;

        // when
        order.Report(connectionLoss);
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

    /// <summary>
    /// A client whose shutdown raises <see cref="MqLogType.ConsumerCancelled"/> on another thread and blocks until the
    /// register's callback returns, as RabbitMQ does when a channel is disposed: a callback that waits on a lock the
    /// shutting-down thread holds never returns.
    /// </summary>
    private sealed class DispatcherCancelOnShutdownClient(IConsumerClient inner) : IConsumerClient
    {
        private static readonly TimeSpan _DispatcherWait = TimeSpan.FromSeconds(10);

        public bool? CancelHandledDuringShutdown { get; private set; }

        public BrokerAddress BrokerAddress => inner.BrokerAddress;

        public Action<LogMessageEventArgs>? OnLogCallback => inner.OnLogCallback;

        public Func<TransportMessage, object?, Task>? OnMessageCallback => inner.OnMessageCallback;

        public ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var onLog = inner.OnLogCallback;
            var dispatcher = new Thread(() =>
                onLog?.Invoke(new LogMessageEventArgs { LogType = MqLogType.ConsumerCancelled, Reason = "shutdown" })
            )
            {
                IsBackground = true,
            };
            dispatcher.Start();
#pragma warning disable CA1849 // The synchronous wait is the transport behavior this double reproduces.
            CancelHandledDuringShutdown = dispatcher.Join(_DispatcherWait);
#pragma warning restore CA1849

            return inner.ShutdownAsync(timeout, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public ValueTask<ICollection<string>> FetchMessageNamesAsync(
            IEnumerable<string> messageNames,
            CancellationToken cancellationToken = default
        ) => inner.FetchMessageNamesAsync(messageNames, cancellationToken);

        public ValueTask SubscribeAsync(
            IEnumerable<string> messageNames,
            CancellationToken cancellationToken = default
        ) => inner.SubscribeAsync(messageNames, cancellationToken);

        public ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            inner.ListeningAsync(timeout, cancellationToken);

        public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
            inner.WaitUntilReadyAsync(cancellationToken);

        public ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default) =>
            inner.CommitAsync(sender, cancellationToken);

        public ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default) =>
            inner.RejectAsync(sender, cancellationToken);

        public ValueTask RejectAsync(object? sender, TimeSpan redeliveryDelay, CancellationToken cancellationToken) =>
            inner.RejectAsync(sender, redeliveryDelay, cancellationToken);

        public ValueTask DeadLetterAsync(
            object? sender,
            string reason,
            string? description,
            CancellationToken cancellationToken = default
        ) => inner.DeadLetterAsync(sender, reason, description, cancellationToken);

        public ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
            inner.PauseAsync(cancellationToken);

        public ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
            inner.ResumeAsync(cancellationToken);

        public void AttachCallbacks(
            Func<TransportMessage, object?, Task>? onMessage,
            Action<LogMessageEventArgs>? onLog
        ) => inner.AttachCallbacks(onMessage, onLog);

        public void AttachReestablishedCallback(Func<CancellationToken, Task>? onReestablished) =>
            inner.AttachReestablishedCallback(onReestablished);
    }
}
