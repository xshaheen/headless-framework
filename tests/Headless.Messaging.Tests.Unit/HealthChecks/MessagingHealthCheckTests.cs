// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.HealthChecks;

/// <summary>
/// A Messaging host's readiness reports whether it can move messages: its lifecycle and broker reachability, paused
/// consumers, and additional outboxes whose database was down at startup.
/// </summary>
public sealed class MessagingHealthCheckTests : TestBase
{
    private const string _MessagingCheck = "messaging";
    private const string _ConsumersCheck = "messaging-consumers";
    private const string _OutboxesCheck = "messaging-outboxes";

    [Fact]
    public async Task should_pass_the_check_command_of_a_host_that_was_never_started()
    {
        // given - the lifecycle checks are unhealthy before start, so check passes only because it skips them
        var host = _BuildHost(_ => { });

        // when
        var exitCode = await host.RunHeadlessAsync(["check"], AbortToken);

        // then
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task should_report_messaging_unhealthy_before_start_and_healthy_after()
    {
        // given
        using var host = _BuildHost(_ => { });

        // when
        var beforeStart = await _CheckAsync(host.Services, _MessagingCheck);
        await host.StartAsync(AbortToken);
        await _WaitStartedAsync(host.Services);
        var afterStart = await _CheckAsync(host.Services, _MessagingCheck);

        // then
        beforeStart.Status.Should().Be(HealthStatus.Unhealthy);
        beforeStart.Description.Should().Be("The 'messaging' dependency probe failed.");
        beforeStart.Data.Should().BeEmpty();
        afterStart.Status.Should().Be(HealthStatus.Healthy);

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_follow_broker_reachability_once_started()
    {
        // given
        await using var provider = _BuildProvider(_ => { });
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var connectivity = provider.GetRequiredService<TransportConnectivity>();

        // when
        for (var i = 0; i < TransportConnectivity.FailureThreshold; i++)
        {
            connectivity.ReportBrokerFailure();
        }

        var unreachable = await _CheckAsync(provider, _MessagingCheck);
        connectivity.ReportBrokerSuccess();
        var reachable = await _CheckAsync(provider, _MessagingCheck);

        // then
        unreachable.Status.Should().Be(HealthStatus.Unhealthy);
        unreachable.Description.Should().Be("The 'messaging' dependency probe failed.");
        unreachable.Data.Should().BeEmpty();
        reachable.Status.Should().Be(HealthStatus.Healthy);

        await ((IHostedService)provider.GetRequiredService<IBootstrapper>()).StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_messaging_unhealthy_once_the_bootstrapper_stops()
    {
        // given - no generic host, so only the bootstrapper's own lifecycle can turn the check
        await using var provider = _BuildProvider(_ => { });
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();
        await bootstrapper.BootstrapAsync(AbortToken);
        (await _CheckAsync(provider, _MessagingCheck)).Status.Should().Be(HealthStatus.Healthy);

        // when
        await ((IHostedService)bootstrapper).StopAsync(AbortToken);
        var stopped = await _CheckAsync(provider, _MessagingCheck);

        // then
        stopped.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task should_report_messaging_unhealthy_once_the_host_starts_shutting_down()
    {
        // given - a hosted service registered after Messaging stops first, so it holds shutdown open before the
        // bootstrapper stops
        var blocker = new BlockingStopService();
        using var host = _BuildHost(_ => { }, services => services.AddHostedService(_ => blocker));
        await host.StartAsync(AbortToken);
        await _WaitStartedAsync(host.Services);
        (await _CheckAsync(host.Services, _MessagingCheck)).Status.Should().Be(HealthStatus.Healthy);

        // when
        var stopTask = host.StopAsync(AbortToken);
        await blocker.StopEntered.Task.WaitAsync(AbortToken);
        var duringShutdown = await _CheckAsync(host.Services, _MessagingCheck);
        var bootstrapperStillStarted = host.Services.GetRequiredService<IBootstrapper>().IsStarted;
        blocker.ReleaseStop.TrySetResult();
        await stopTask;

        // then
        bootstrapperStillStarted.Should().BeTrue("the shutdown signal alone must turn the check");
        duringShutdown.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task should_report_consumers_degraded_while_a_circuit_is_forced_open_and_healthy_once_half_open()
    {
        // given - an operator pauses the payments consumer on the Bus lane
        var time = new FakeTimeProvider();
        await using var provider = _BuildProvider(
            setup =>
            {
                setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30);
                setup.AddConsumer<PaymentsConsumer>();
            },
            services =>
            {
                services.AddSingleton<TimeProvider>(time);
                services.ConfigureMessaging(messaging => messaging.Message<PaymentReceived>("tests.payment-received"));
                services.AddSingleton<PaymentsConsumer>();
            }
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var monitor = provider.GetRequiredService<ICircuitBreakerMonitor>();
        var closed = await _CheckAsync(provider, _ConsumersCheck);

        // when
        (await monitor.ForceOpenAsync(MessageLane.Bus, PaymentsConsumer.Identity, AbortToken))
            .Should()
            .BeTrue();
        var open = await _CheckAsync(provider, _ConsumersCheck);
        var messagingWhileOpen = await _CheckAsync(provider, _MessagingCheck);
        time.Advance(TimeSpan.FromSeconds(31));
        monitor.GetState(MessageLane.Bus, PaymentsConsumer.Identity).Should().Be(CircuitBreakerState.HalfOpen);
        var halfOpen = await _CheckAsync(provider, _ConsumersCheck);

        // then
        closed.Status.Should().Be(HealthStatus.Healthy);
        open.Status.Should().Be(HealthStatus.Degraded);
        open.Description.Should().Be("The 'messaging-consumers' dependency probe failed.");
        open.Data.Should().BeEmpty();
        messagingWhileOpen.Status.Should().Be(HealthStatus.Healthy, "a paused consumer does not make the host unready");
        halfOpen.Status.Should().Be(HealthStatus.Healthy);

        await ((IHostedService)provider.GetRequiredService<IBootstrapper>()).StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_outboxes_degraded_while_an_additional_outbox_is_not_initialized()
    {
        // given - the shipping database is down at startup, so its outbox stays uninitialized
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing", initialized: false);
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping", initialized: false);
        shipping
            .Initializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new TimeoutException("shipping is down")));
        await using var provider = _BuildProvider(
            setup => setup.AddOutbox().UseStorage("AddOutbox().UseShipping()", _ => shipping),
            services =>
            {
                services.RemoveAll<MessagingOutboxes>();
                services.AddSingleton(
                    AdditionalOutboxDoubles.CreateOutboxes(
                        AdditionalOutboxDoubles.CreateRelationalStorage("orders"),
                        billing,
                        shipping
                    )
                );
            }
        );
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // when
        var outboxes = await _CheckAsync(provider, _OutboxesCheck);
        var messaging = await _CheckAsync(provider, _MessagingCheck);

        // then
        billing.IsInitialized.Should().BeTrue();
        outboxes.Status.Should().Be(HealthStatus.Degraded);
        outboxes.Description.Should().Be("The 'messaging-outboxes' dependency probe failed.");
        outboxes.Data.Should().BeEmpty();
        messaging.Status.Should().Be(HealthStatus.Healthy);

        await ((IHostedService)provider.GetRequiredService<IBootstrapper>()).StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_outboxes_healthy_when_every_additional_outbox_is_initialized()
    {
        // given
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing", initialized: false);
        await using var provider = _BuildProvider(
            setup => setup.AddOutbox().UseStorage("AddOutbox().UseBilling()", _ => billing),
            services =>
            {
                services.RemoveAll<MessagingOutboxes>();
                services.AddSingleton(
                    AdditionalOutboxDoubles.CreateOutboxes(
                        AdditionalOutboxDoubles.CreateRelationalStorage("orders"),
                        billing
                    )
                );
            }
        );
        var beforeStart = await _CheckAsync(provider, _OutboxesCheck);

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        var afterStart = await _CheckAsync(provider, _OutboxesCheck);

        // then
        beforeStart.Status.Should().Be(HealthStatus.Degraded);
        afterStart.Status.Should().Be(HealthStatus.Healthy);

        await ((IHostedService)provider.GetRequiredService<IBootstrapper>()).StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_register_ready_checks_tagged_messaging_and_no_outboxes_check_without_additional_outboxes()
    {
        // given
        await using var provider = _BuildProvider(_ => { });

        // when
        var registrations = _Registrations(provider);

        // then
        registrations
            .Where(r => r.Name.StartsWith("messaging", StringComparison.Ordinal))
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo([_MessagingCheck, _ConsumersCheck]);

        foreach (var registration in registrations.Where(r => r.Name.StartsWith("messaging", StringComparison.Ordinal)))
        {
            registration
                .Tags.Should()
                .BeEquivalentTo([
                    HeadlessHealthCheckTags.Ready,
                    HeadlessHealthCheckTags.Headless,
                    HeadlessHealthCheckTags.Messaging,
                    HeadlessHealthCheckTags.HostState,
                ]);
        }

        _Registration(provider, _MessagingCheck).FailureStatus.Should().Be(HealthStatus.Unhealthy);
        _Registration(provider, _ConsumersCheck).FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task should_register_the_outboxes_check_as_degraded_when_additional_outboxes_exist()
    {
        // given
        await using var provider = _BuildProvider(setup =>
            setup.AddOutbox().UseStorage("AddOutbox().UseBilling()", _ => AdditionalOutboxDoubles.CreateOutbox("b"))
        );

        // when
        var registration = _Registration(provider, _OutboxesCheck);

        // then
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
        registration.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Messaging]);
    }

    [Fact]
    public async Task should_let_the_application_override_the_degraded_default()
    {
        // given - configured before AddHeadlessMessaging, which must still leave the application's choice in place
        await using var provider = _BuildProvider(
            _ => { },
            before: services =>
                services.ConfigureHeadlessHealthCheck(
                    _ConsumersCheck,
                    options => options.FailureStatus = HealthStatus.Unhealthy
                )
        );

        // when
        var registration = _Registration(provider, _ConsumersCheck);

        // then
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task should_drop_the_messaging_checks_through_remove_health_checks()
    {
        // given
        await using var provider = _BuildProvider(
            setup =>
                setup
                    .AddOutbox()
                    .UseStorage("AddOutbox().UseBilling()", _ => AdditionalOutboxDoubles.CreateOutbox("b")),
            before: services =>
                services.RemoveHealthChecks(registration =>
                    registration.Name.StartsWith("messaging", StringComparison.Ordinal)
                )
        );

        // when
        var names = _Registrations(provider).Select(r => r.Name);

        // then
        names.Should().NotContain(name => name.StartsWith("messaging", StringComparison.Ordinal));
    }

    private static ServiceProvider _BuildProvider(
        Action<MessagingSetupBuilder> configureMessaging,
        Action<IServiceCollection>? after = null,
        Action<IServiceCollection>? before = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        before?.Invoke(services);
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            configureMessaging(setup);
        });
        after?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static IHost _BuildHost(
        Action<MessagingSetupBuilder> configureMessaging,
        Action<IServiceCollection>? afterMessaging = null
    )
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                // Keeps the test from installing the process-wide Ctrl+C / ProcessExit handlers ConsoleLifetime adds.
                services.AddSingleton<IHostLifetime, NoopHostLifetime>();
                services.AddHeadlessMessaging(setup =>
                {
                    setup.UseInMemory();
                    setup.UseProcessLocalInMemoryStorage();
                    configureMessaging(setup);
                });
                afterMessaging?.Invoke(services);
            })
            .Build();
    }

    // The bootstrapper is a BackgroundService, so host start returns before its bootstrap completes.
    private static async Task _WaitStartedAsync(IServiceProvider services)
    {
        var bootstrapper = services.GetRequiredService<IBootstrapper>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!bootstrapper.IsStarted)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task<HealthReportEntry> _CheckAsync(IServiceProvider services, string name)
    {
        var report = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => string.Equals(registration.Name, name, StringComparison.Ordinal),
                AbortToken
            );

        return report.Entries[name];
    }

    private static List<HealthCheckRegistration> _Registrations(IServiceProvider provider)
    {
        return [.. provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations];
    }

    private static HealthCheckRegistration _Registration(IServiceProvider provider, string name)
    {
        return _Registrations(provider).Single(r => string.Equals(r.Name, name, StringComparison.Ordinal));
    }

    [BusConsumer(Identity)]
    private sealed class PaymentsConsumer : IConsume<PaymentReceived>
    {
        public const string Identity = "payments";

        public ValueTask ConsumeAsync(ConsumeContext<PaymentReceived> context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed record PaymentReceived;

    private sealed class BlockingStopService : IHostedService
    {
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            StopEntered.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
