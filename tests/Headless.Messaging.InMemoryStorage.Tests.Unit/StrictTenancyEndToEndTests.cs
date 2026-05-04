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
/// These tests assert what the guard does — not what happens downstream. A successful guard
/// pass-through that hits a transport-level failure (e.g., no subscriber registered for the
/// topic) is still a valid guard outcome; we only verify that <see cref="MissingTenantContextException"/>
/// is or is not raised based on the configured posture and ambient context.
/// </remarks>
public sealed class StrictTenancyEndToEndTests : TestBase
{
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
    public async Task should_pass_guard_when_ambient_tenant_set_via_change_scope()
    {
        // given - simulates IHostedService wrapping publish in ICurrentTenant.Change(...)
        await using var provider = _BuildProvider(tenantContextRequired: true);
        var publisher = provider.GetRequiredService<IDirectPublisher>();
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();

        // when - the guard runs first; downstream transport may still fail (no subscriber)
        // but the guard itself must not throw MissingTenantContextException
        Func<Task> act = async () =>
        {
            using (currentTenant.Change("acme"))
            {
                await publisher.PublishAsync(new OrderPlacedMessage("o-1"), options: null);
            }
        };

        // then
        var exceptionAssertion = await act.Should().ThrowAsync<Exception>();
        exceptionAssertion.Which.Should().NotBeOfType<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_pass_guard_when_publish_options_tenant_set_without_ambient()
    {
        // given - PublishOptions.TenantId wins over (missing) ambient
        await using var provider = _BuildProvider(tenantContextRequired: true);
        var publisher = provider.GetRequiredService<IDirectPublisher>();

        // when
        var act = () => publisher.PublishAsync(new OrderPlacedMessage("o-1"), new PublishOptions { TenantId = "acme" });

        // then - guard passes; transport-layer failure (if any) is not a U10 concern
        var exceptionAssertion = await act.Should().ThrowAsync<Exception>();
        exceptionAssertion.Which.Should().NotBeOfType<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_skip_guard_when_strict_tenancy_disabled_by_default()
    {
        // given - default posture: TenantContextRequired = false; guard never runs
        await using var provider = _BuildProvider(tenantContextRequired: false);
        var publisher = provider.GetRequiredService<IDirectPublisher>();

        // when
        var act = () => publisher.PublishAsync(new OrderPlacedMessage("o-1"), options: null);

        // then - guard skipped; any downstream exception is not MissingTenantContextException
        var exceptionAssertion = await act.Should().ThrowAsync<Exception>();
        exceptionAssertion.Which.Should().NotBeOfType<MissingTenantContextException>();
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
}
