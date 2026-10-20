// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tests.Registration;

namespace Tests.Transport;

/// <summary>
/// The running host checks the shared broker topology once, in one batch per transport, as a warm-up that holds
/// readiness until the topology exists, instead of each consumer client checking its own objects as it connects.
/// </summary>
public sealed class TransportTopologyWarmupTests : TestBase
{
    private const string _MissingQueue = "RabbitMQ queue 'queue.orders' does not exist. Run 'resources provision'.";

    [Fact]
    public async Task should_check_each_transport_once_with_the_registered_topology_when_warming_up()
    {
        // given
        var first = new RecordingProvisioner("First");
        var second = new RecordingProvisioner("Second");
        await using var provider = _BuildProvider(first, second);
        var warmup = provider.GetServices<IWarmup>().Should().ContainSingle(w => w is TransportTopologyWarmup).Which;

        // when
        await warmup.WarmUpAsync(AbortToken);

        // then
        warmup.GatesReadiness.Should().BeTrue();
        warmup.Name.Should().Be("MessagingTopology");
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
        var warmup = provider.GetServices<IWarmup>().OfType<TransportTopologyWarmup>().Single();

        // when
        var act = () => warmup.WarmUpAsync(AbortToken);

        // then
        var failure = await act.Should().ThrowAsync<AggregateException>();
        failure.Which.InnerExceptions.Select(e => e.Message).Should().Equal(_MissingQueue, "second missing");
        second.Checks.Should().Be(1, "a failing transport does not hide another's state");
    }

    [Fact]
    public async Task should_rethrow_the_transports_own_error_when_one_check_fails()
    {
        // given
        var provisioner = new RecordingProvisioner("Only")
        {
            Failure = new ResourceProvisioningException(_MissingQueue),
        };
        await using var provider = _BuildProvider(provisioner);
        var warmup = provider.GetServices<IWarmup>().OfType<TransportTopologyWarmup>().Single();

        // when
        var act = () => warmup.WarmUpAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage(_MissingQueue);
    }

    [Fact]
    public async Task should_report_unready_naming_the_missing_queue_until_the_topology_exists()
    {
        // given
        var provisioner = new RecordingProvisioner("RabbitMQ")
        {
            Failure = new ResourceProvisioningException(_MissingQueue),
        };
        using var host = _BuildHost(provisioner);

        // when
        await host.StartAsync(AbortToken);
        var unready = await _WaitForCheckAsync(
            host.Services,
            entry => entry.Exception?.Message.Contains("queue.orders", StringComparison.Ordinal) == true
        );

        // then
        unready.Status.Should().Be(HealthStatus.Unhealthy);
        unready.Exception!.Message.Should().Contain("MessagingTopology").And.Contain("Run 'resources provision'");

        // and when - the deployment creates the topology, and the next retry passes
        provisioner.Failure = null;
        var ready = await _WaitForCheckAsync(host.Services, entry => entry.Status == HealthStatus.Healthy);

        // then
        ready.Status.Should().Be(HealthStatus.Healthy);
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_register_no_topology_check_when_verify_topology_is_off()
    {
        // given
        var provisioner = new RecordingProvisioner("RabbitMQ");

        // when
        await using var provider = _BuildProvider(setup => setup.Options.VerifyTopology = false, provisioner);

        // then
        provider.GetServices<IWarmup>().Should().NotContain(w => w is TransportTopologyWarmup);
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .NotContain(r => r.Name == HeadlessWarmupServiceCollectionExtensions.HealthCheckName);
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.VerifyTopology.Should().BeFalse();
    }

    [Fact]
    public void should_verify_topology_by_default()
    {
        new MessagingOptions().VerifyTopology.Should().BeTrue();
    }

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

    private static IHost _BuildHost(RecordingProvisioner provisioner)
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                // Keeps the test from installing the process-wide Ctrl+C / ProcessExit handlers ConsoleLifetime adds.
                services.AddSingleton<IHostLifetime, NoopHostLifetime>();
                _AddMessaging(services, _ => { }, [provisioner]);
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
        const string name = HeadlessWarmupServiceCollectionExtensions.HealthCheckName;

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

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
