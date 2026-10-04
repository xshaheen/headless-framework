// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.IntegrationTests;

/// <summary>
/// Tenant propagation on the Queue lane through a real in-memory host: a plain enqueue stamps the ambient tenant and a
/// plain Queue consumer runs inside that tenant's scope, exactly as the Bus lane does.
/// </summary>
public sealed class QueueTenantPropagationIntegrationTests : TestBase
{
    private const string _MessageName = "tests.queue-tenant-probe";

    [Fact]
    public async Task should_stamp_the_ambient_tenant_on_a_plain_enqueue_and_run_the_queue_consumer_under_it()
    {
        // given
        await using var provider = await _CreateStartedProviderAsync(propagateTenant: true);
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        var recorder = provider.GetRequiredService<TenantProbeRecorder>();

        // when
        using (currentTenant.Change("tenant-a"))
        {
            await provider.GetRequiredService<IQueue>().EnqueueAsync(new TenantProbeMessage("m1"), AbortToken);
        }

        var observed = await recorder.WaitForConsumeAsync(AbortToken);

        // then
        observed.EnvelopeTenant.Should().Be("tenant-a");
        observed.ContextTenant.Should().Be("tenant-a");
        observed.AmbientTenantInConsumer.Should().Be("tenant-a");
        observed.AmbientTenantBeforeScope.Should().BeNull();
        observed.AmbientTenantAfterScope.Should().BeNull();
    }

    [Fact]
    public async Task should_keep_an_explicit_queue_tenant_over_the_ambient_one()
    {
        // given
        await using var provider = await _CreateStartedProviderAsync(propagateTenant: true);
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        var recorder = provider.GetRequiredService<TenantProbeRecorder>();

        // when
        using (currentTenant.Change("tenant-a"))
        {
            await provider
                .GetRequiredService<IQueue>()
                .EnqueueAsync(new TenantProbeMessage("m1"), new QueueOptions { TenantId = "tenant-b" }, AbortToken);
        }

        var observed = await recorder.WaitForConsumeAsync(AbortToken);

        // then
        observed.EnvelopeTenant.Should().Be("tenant-b");
        observed.AmbientTenantInConsumer.Should().Be("tenant-b");
    }

    [Fact]
    public async Task should_not_stamp_or_restore_a_tenant_when_the_host_does_not_propagate_tenants()
    {
        // given
        await using var provider = await _CreateStartedProviderAsync(propagateTenant: false);
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        var recorder = provider.GetRequiredService<TenantProbeRecorder>();

        // when
        using (currentTenant.Change("tenant-a"))
        {
            await provider.GetRequiredService<IQueue>().EnqueueAsync(new TenantProbeMessage("m1"), AbortToken);
        }

        var observed = await recorder.WaitForConsumeAsync(AbortToken);

        // then
        observed.EnvelopeTenant.Should().BeNull();
        observed.ContextTenant.Should().BeNull();
        observed.AmbientTenantInConsumer.Should().BeNull();
    }

    private async Task<ServiceProvider> _CreateStartedProviderAsync(bool propagateTenant)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging(logging =>
        {
            logging.AddProvider(LoggerProvider);
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        if (propagateTenant)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        }

        builder.Services.AddSingleton<TenantProbeRecorder>();
        builder.Services.ConfigureMessaging(messaging => messaging.Message<TenantProbeMessage>(_MessageName));
        builder
            .Services.AddHeadlessMessaging(setup =>
            {
                setup.AddConsumer<TenantProbeConsumer>();
                setup.UseInMemory();
                setup.UseProcessLocalInMemoryStorage();
            })
            // Runs outside the tenant middleware, so it sees the ambient tenant before the scope opens and after it
            // closes.
            .AddQueueConsumeMiddleware<TenantScopeObservingMiddleware>()
            .WithPriority(-2000);

        var provider = builder.Services.BuildServiceProvider();
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private sealed record TenantProbeMessage(string Id);

    private sealed record TenantObservation(
        string? EnvelopeTenant,
        string? ContextTenant,
        string? AmbientTenantInConsumer,
        string? AmbientTenantBeforeScope,
        string? AmbientTenantAfterScope
    );

    /// <summary>
    /// Collects one consume's observations in the order the pipeline makes them: the observing middleware before the
    /// tenant scope opens, the consumer inside it, and the middleware again after it closes.
    /// </summary>
    private sealed class TenantProbeRecorder
    {
        private readonly TaskCompletionSource<TenantObservation> _consumed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        private string? _ambientBeforeScope;
        private string? _envelopeTenant;
        private string? _contextTenant;
        private string? _ambientInConsumer;

        public void RecordBeforeScope(string? ambientTenant)
        {
            _ambientBeforeScope = ambientTenant;
        }

        public void RecordConsumer(string? envelopeTenant, string? contextTenant, string? ambientTenant)
        {
            _envelopeTenant = envelopeTenant;
            _contextTenant = contextTenant;
            _ambientInConsumer = ambientTenant;
        }

        public void RecordAfterScope(string? ambientTenant)
        {
            _consumed.TrySetResult(
                new TenantObservation(
                    _envelopeTenant,
                    _contextTenant,
                    _ambientInConsumer,
                    _ambientBeforeScope,
                    ambientTenant
                )
            );
        }

        public Task<TenantObservation> WaitForConsumeAsync(CancellationToken cancellationToken)
        {
            return _consumed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class TenantScopeObservingMiddleware(TenantProbeRecorder recorder, ICurrentTenant currentTenant)
        : IConsumeMiddleware<ConsumeContext>
    {
        public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
        {
            recorder.RecordBeforeScope(currentTenant.Id);
            await next().ConfigureAwait(false);
            recorder.RecordAfterScope(currentTenant.Id);
        }
    }

    [QueueConsumer("tests.queue-tenant-probe.consumer")]
    private sealed class TenantProbeConsumer(TenantProbeRecorder recorder, ICurrentTenant currentTenant)
        : IConsume<TenantProbeMessage>
    {
        public ValueTask ConsumeAsync(ConsumeContext<TenantProbeMessage> context, CancellationToken cancellationToken)
        {
            context.Headers.TryGetValue(Headers.TenantId, out var envelopeTenant);
            recorder.RecordConsumer(envelopeTenant, context.TenantId, currentTenant.Id);
            return ValueTask.CompletedTask;
        }
    }
}
