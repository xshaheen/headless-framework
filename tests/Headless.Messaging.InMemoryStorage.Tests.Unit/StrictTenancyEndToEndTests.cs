// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.InMemoryStorage;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// End-to-end coverage for the U10 (#238) strict-tenancy publish guard wired through the full
/// <c>AddHeadlessMessaging</c> DI pipeline against the in-memory transport. Complements the
/// factory-level unit tests in <c>StrictTenancyPublishGuardTests</c> by verifying that a real
/// <see cref="IDirectPublisher"/> resolved from the container honors the guard contract.
/// </summary>
/// <remarks>
/// Success-path tests bootstrap the messaging pipeline, attach a runtime consumer for
/// <see cref="OrderPlacedMessage"/>, publish, and assert that the message is actually delivered
/// to the consumer with the expected payload and end-to-end <c>TenantId</c> propagation. The
/// failure-path test asserts that <see cref="MissingTenantContextException"/> is raised when
/// strict tenancy is enabled and no tenant is resolvable.
/// </remarks>
public sealed class StrictTenancyEndToEndTests : TestBase
{
    private static readonly TimeSpan _DeliveryTimeout = TimeSpan.FromSeconds(5);

    private sealed record OrderPlacedMessage(string OrderId);

    [Fact]
    public async Task should_throw_missing_tenant_context_when_strict_tenancy_enabled_and_no_ambient_tenant()
    {
        // given - full DI pipeline with strict tenancy on, no ambient tenant resolved
        await using var provider = _BuildProvider(tenantContextRequired: true);
        var publisher = provider.GetRequiredService<IDirectPublisher>();

        // when
        var act = () => publisher.PublishAsync(new OrderPlacedMessage("o-1"), options: null);

        // then
        await act.Should()
            .ThrowAsync<MissingTenantContextException>()
            .WithMessage("*Publish requires an ambient tenant context*");
    }

    [Fact]
    public async Task should_deliver_message_with_ambient_tenant_when_strict_tenancy_enabled()
    {
        // given - simulates IHostedService wrapping publish in ICurrentTenant.Change(...)
        await using var provider = await _BuildStartedProviderAsync(tenantContextRequired: true);
        var publisher = provider.GetRequiredService<IDirectPublisher>();
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();
        var probe = await _AttachProbeAsync(provider);

        // when - guard passes, message flows through the transport to the runtime consumer
        using (currentTenant.Change("acme"))
        {
            await publisher.PublishAsync(new OrderPlacedMessage("o-1"), options: null, AbortToken);
        }

        var consumed = await probe.WaitForMessageAsync(AbortToken);

        // then - message and TenantId propagated end-to-end
        consumed.Message.OrderId.Should().Be("o-1");
        consumed.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task should_deliver_message_with_publish_options_tenant_when_ambient_unset()
    {
        // given - PublishOptions.TenantId wins over (missing) ambient
        await using var provider = await _BuildStartedProviderAsync(tenantContextRequired: true);
        var publisher = provider.GetRequiredService<IDirectPublisher>();
        var probe = await _AttachProbeAsync(provider);

        // when - explicit tenant on PublishOptions satisfies the guard
        await publisher.PublishAsync(
            new OrderPlacedMessage("o-1"),
            new PublishOptions { TenantId = "acme" },
            AbortToken
        );

        var consumed = await probe.WaitForMessageAsync(AbortToken);

        // then - message and TenantId propagated end-to-end
        consumed.Message.OrderId.Should().Be("o-1");
        consumed.TenantId.Should().Be("acme");
    }

    [Fact]
    public async Task should_deliver_message_without_tenant_when_strict_tenancy_disabled_by_default()
    {
        // given - default posture: TenantContextRequired = false; guard never runs
        await using var provider = await _BuildStartedProviderAsync(tenantContextRequired: false);
        var publisher = provider.GetRequiredService<IDirectPublisher>();
        var probe = await _AttachProbeAsync(provider);

        // when - no tenant on either side; guard skipped entirely
        await publisher.PublishAsync(new OrderPlacedMessage("o-1"), options: null, AbortToken);

        var consumed = await probe.WaitForMessageAsync(AbortToken);

        // then - message delivered, no tenant propagated
        consumed.Message.OrderId.Should().Be("o-1");
        consumed.TenantId.Should().BeNull();
    }

    private async Task<ServiceProvider> _BuildStartedProviderAsync(bool tenantContextRequired)
    {
        var provider = _BuildProvider(tenantContextRequired);
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        return provider;
    }

    private async Task<_OrderPlacedProbe> _AttachProbeAsync(ServiceProvider provider)
    {
        var runtimeSubscriber = provider.GetRequiredService<IRuntimeSubscriber>();
        var probe = new _OrderPlacedProbe();

        await runtimeSubscriber.SubscribeAsync<OrderPlacedMessage>(
            probe.HandleAsync,
            new RuntimeSubscriptionOptions { Topic = "orders.placed", Group = "orders.placed" },
            AbortToken
        );

        return probe;
    }

    private static ServiceProvider _BuildProvider(bool tenantContextRequired)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();
            options.WithTopicMapping<OrderPlacedMessage>("orders.placed");
            options.TenantContextRequired = tenantContextRequired;
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Captures the first delivered <see cref="OrderPlacedMessage"/> consume context via a
    /// <see cref="TaskCompletionSource{T}"/>, allowing tests to await delivery deterministically
    /// with a tight timeout.
    /// </summary>
    private sealed class _OrderPlacedProbe
    {
        private readonly TaskCompletionSource<ConsumeContext<OrderPlacedMessage>> _received = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask HandleAsync(
            ConsumeContext<OrderPlacedMessage> context,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            _received.TrySetResult(context);
            return ValueTask.CompletedTask;
        }

        public async Task<ConsumeContext<OrderPlacedMessage>> WaitForMessageAsync(CancellationToken cancellationToken)
        {
            return await _received.Task.WaitAsync(_DeliveryTimeout, cancellationToken);
        }
    }
}
