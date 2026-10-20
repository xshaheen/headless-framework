// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Tests.Registration;

namespace Tests.Transport;

/// <summary>
/// Each transport's shared broker topology is a provisioned resource whose startup check gates readiness: the running
/// host checks it once, in one batch per transport, through the <c>Resources</c> warm-up, instead of each consumer client
/// checking its own objects as it connects.
/// </summary>
public sealed class TransportTopologyStartupCheckTests : TestBase
{
    private const string _MissingQueue = "RabbitMQ queue 'queue.orders' does not exist. Run 'resources provision'.";

    [Fact]
    public async Task should_check_each_transport_once_with_the_registered_topology_when_warming_up()
    {
        // given
        var first = new RecordingProvisioner("First");
        var second = new RecordingProvisioner("Second");
        await using var provider = _BuildProvider(first, second);
        var warmup = _ResourcesWarmup(provider);

        // when
        await warmup.WarmUpAsync(AbortToken);

        // then
        warmup.GatesReadiness.Should().BeTrue();
        first.Checks.Should().Be(1);
        second.Checks.Should().Be(1);
        first
            .LastTopology!.Subscriptions.Select(s => s.SubscriptionName)
            .Should()
            .Contain([TestConsumers.InvoiceProjection, TestConsumers.PriceCache]);
        second.LastTopology.Should().BeSameAs(first.LastTopology, "one host builds the topology once per check");
    }

    [Fact]
    public async Task should_check_every_transport_and_report_each_failure_when_several_fail()
    {
        // given
        var first = new RecordingProvisioner("First") { Failure = new ResourceProvisioningException(_MissingQueue) };
        var second = new RecordingProvisioner("Second")
        {
            Failure = new ResourceProvisioningException("second missing"),
        };
        await using var provider = _BuildProvider(first, second);

        // when
        var act = () => _ResourcesWarmup(provider).WarmUpAsync(AbortToken);

        // then
        var failure = await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*First topology: *queue.orders*Second topology: second missing*");
        failure.Which.Failures.Select(e => e.Message).Should().Equal(_MissingQueue, "second missing");
        second.Checks.Should().Be(1, "a failing transport does not hide another's state");
    }

    [Fact]
    public async Task should_keep_the_transports_own_error_when_one_check_fails()
    {
        // given
        var provisioner = new RecordingProvisioner("Only")
        {
            Failure = new ResourceProvisioningException(_MissingQueue),
        };
        await using var provider = _BuildProvider(provisioner);

        // when
        var act = () => _ResourcesWarmup(provider).WarmUpAsync(AbortToken);

        // then
        (await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*Only topology: *queue.orders*"))
            .WithInnerException<ResourceProvisioningException>()
            .WithMessage(_MissingQueue);
    }

    [Fact]
    public async Task should_report_unready_naming_the_missing_queue_until_the_topology_exists()
    {
        // given
        var provisioner = new RecordingProvisioner("RabbitMQ")
        {
            Failure = new ResourceProvisioningException(_MissingQueue),
        };
        using var host = _BuildHost(_ => { }, provisioner);

        // when
        await host.StartAsync(AbortToken);
        var unready = await _WaitForCheckAsync(
            host.Services,
            entry => entry.Exception?.Message.Contains("queue.orders", StringComparison.Ordinal) == true
        );

        // then
        unready.Status.Should().Be(HealthStatus.Unhealthy);
        unready.Exception!.Message.Should().Contain("Resources").And.Contain("Run 'resources provision'");

        // and when - the deployment creates the topology, and the next retry passes
        provisioner.Failure = null;
        var ready = await _WaitForCheckAsync(host.Services, entry => entry.Status == HealthStatus.Healthy);

        // then
        ready.Status.Should().Be(HealthStatus.Healthy);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_be_ready_without_checking_the_topology_when_the_application_sets_the_kind_to_none()
    {
        // given
        var provisioner = new RecordingProvisioner("RabbitMQ")
        {
            Failure = new ResourceProvisioningException(_MissingQueue),
        };
        using var host = _BuildHost(
            services =>
                services.ConfigureResourceProvisioning(o => o.StartupChecks["RabbitMQ"] = ResourceStartupCheck.None),
            provisioner
        );

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        await host.StopAsync(AbortToken);

        // then
        provisioner.Checks.Should().Be(0);
    }

    [Fact]
    public async Task should_fail_the_start_when_a_transport_asks_to_fail_start_and_its_topology_is_missing()
    {
        // given
        var provisioner = new RecordingProvisioner("RabbitMQ")
        {
            Failure = new ResourceProvisioningException(_MissingQueue),
            Check = ResourceStartupCheck.FailStart,
        };
        using var host = _BuildHost(_ => { }, provisioner);

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*RabbitMQ topology: *queue.orders*");
    }

    [Fact]
    public void should_gate_readiness_by_default()
    {
        ITransportTopologyProvisioner provisioner = new DefaultCheckProvisioner();

        provisioner.StartupCheck.Should().Be(ResourceStartupCheck.GateReadiness);
    }

    private static IWarmup _ResourcesWarmup(IServiceProvider provider) =>
        provider.GetServices<IWarmup>().Should().ContainSingle(w => w.Name == "Resources").Which;

    private static ServiceProvider _BuildProvider(params RecordingProvisioner[] provisioners) =>
        _BuildProvider(_ => { }, provisioners);

    private static ServiceProvider _BuildProvider(
        Action<MessagingSetupBuilder> configure,
        params RecordingProvisioner[] provisioners
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _AddMessaging(services, configure, provisioners);

        return services.BuildServiceProvider();
    }

    private static IHost _BuildHost(Action<IServiceCollection> configure, RecordingProvisioner provisioner)
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                // Keeps the test from installing the process-wide Ctrl+C / ProcessExit handlers ConsoleLifetime adds.
                services.AddSingleton<IHostLifetime, NoopHostLifetime>();
                _AddMessaging(services, _ => { }, [provisioner]);
                configure(services);
            })
            .Build();
    }

    private static void _AddMessaging(
        IServiceCollection services,
        Action<MessagingSetupBuilder> configure,
        RecordingProvisioner[] provisioners
    )
    {
        services.AddSingleton<HostControlProbe>();

        foreach (var provisioner in provisioners)
        {
            // Not TryAddEnumerable: it keeps one registration per implementation type, and these share one.
            services.AddSingleton<ITransportTopologyProvisioner>(provisioner);
        }

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.AddModule<BillingModule>().AddModule<BillingPriceCacheModule>().AddModule<BillingQueueModule>();
            configure(setup);
        });
    }

    // The warm-up retries on a one-second back-off, so wait on the check's state rather than a fixed delay.
    private static async Task<HealthReportEntry> _WaitForCheckAsync(
        IServiceProvider services,
        Func<HealthReportEntry, bool> condition
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        const string name = HeadlessHealthCheckNames.Warmup;

        while (true)
        {
            var report = await services
                .GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(r => string.Equals(r.Name, name, StringComparison.Ordinal), timeout.Token);
            var entry = report.Entries[name];

            if (condition(entry))
            {
                return entry;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private sealed class RecordingProvisioner(string broker) : ITransportTopologyProvisioner
    {
        private int _checks;

        // Changed by the test while the warm-up retries on another thread.
        public Exception? Failure
        {
            get => Volatile.Read(ref field);
            set => Volatile.Write(ref field, value);
        }

        public int Checks => Volatile.Read(ref _checks);

        public MessagingTopology? LastTopology { get; private set; }

        public string Kind => broker;

        public ResourceStartupCheck Check { get; init; } = ResourceStartupCheck.GateReadiness;

        public ResourceStartupCheck StartupCheck => Check;

        public Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _checks);
            LastTopology = topology;

            return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
            MessagingTopology topology,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
    }

    private sealed class DefaultCheckProvisioner : ITransportTopologyProvisioner
    {
        public string Kind => "Default";

        public Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
            MessagingTopology topology,
            CancellationToken cancellationToken
        ) => Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);
    }

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
