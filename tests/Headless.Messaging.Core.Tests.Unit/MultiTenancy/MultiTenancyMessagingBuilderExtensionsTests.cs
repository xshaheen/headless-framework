// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.MultiTenancy;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Tests covering <see cref="MultiTenancyMessagingBuilderExtensions.AddTenantPropagation"/>
/// — startup-time validation that fails fast when only the framework's fallback
/// <see cref="NullCurrentTenant"/> is registered.
/// </summary>
public sealed class MultiTenancyMessagingBuilderExtensionsTests : TestBase
{
    [Fact]
    public void should_register_tenant_propagation_filters_from_headless_tenancy_root()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));

        // then
        builder.Services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IConsumeFilter)
                && descriptor.ImplementationType == typeof(TenantPropagationConsumeFilter)
            )
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
        builder.Services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IPublishFilter)
                && descriptor.ImplementationType == typeof(TenantPropagationPublishFilter)
            )
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);

        var manifest = builder.Services.GetOrAddTenantPostureManifest();
        var seam = manifest.GetSeam("Messaging");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatuses.Propagating);
        seam.Capabilities.Should().BeEquivalentTo("propagate-tenant");
    }

    [Fact]
    public void should_enable_strict_publish_from_headless_tenancy_root_regardless_of_messaging_setup_order()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<ICurrentTenant>(Substitute.For<ICurrentTenant>());

        // when
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
        );
        builder.Services.AddHeadlessMessaging(_ => { });

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        options.TenantContextRequired.Should().BeTrue();

        var manifest = builder.Services.GetOrAddTenantPostureManifest();
        var seam = manifest.GetSeam("Messaging");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatuses.Enforcing);
        seam.Capabilities.Should().BeEquivalentTo("propagate-tenant", "require-tenant-on-publish");
    }

    [Fact]
    public void should_not_duplicate_tenant_propagation_filters_when_root_and_builder_are_both_used()
    {
        // given
        var rootBuilder = Host.CreateApplicationBuilder();

        // when
        rootBuilder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        new MessagingBuilder(rootBuilder.Services).AddTenantPropagation();

        // then
        rootBuilder
            .Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IConsumeFilter)
                && descriptor.ImplementationType == typeof(TenantPropagationConsumeFilter)
            )
            .Should()
            .ContainSingle();
        rootBuilder
            .Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IPublishFilter)
                && descriptor.ImplementationType == typeof(TenantPropagationPublishFilter)
            )
            .Should()
            .ContainSingle();
        rootBuilder
            .Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType?.Name == "TenantPropagationStartupValidator"
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task should_throw_when_AddTenantPropagation_called_without_real_ICurrentTenant_implementation()
    {
        // given — only the framework's fallback NullCurrentTenant is registered.
        // AddTenantPropagation registers its hosted-service validator; StartAsync should
        // throw a diagnostic InvalidOperationException pointing to tenant setup APIs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        var builder = new MessagingBuilder(services);
        builder.AddTenantPropagation();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var validator = hostedServices.Single(s => s.GetType().Name == "TenantPropagationStartupValidator");

        // when
        var act = async () => await validator.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*NullCurrentTenant*AddHeadlessInfrastructure*AddHeadlessMultiTenancy*");
    }

    [Fact]
    public async Task should_throw_when_root_tenant_propagation_configured_without_real_ICurrentTenant_implementation()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var validator = hostedServices.Single(s => s.GetType().Name == "TenantPropagationStartupValidator");

        // when
        var act = async () => await validator.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*NullCurrentTenant*AddHeadlessInfrastructure*AddHeadlessMultiTenancy*");
    }

    [Fact]
    public async Task should_not_throw_when_real_ICurrentTenant_is_registered()
    {
        // given — a real (non-null) ICurrentTenant is registered before AddTenantPropagation runs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(Substitute.For<ICurrentTenant>());
        var builder = new MessagingBuilder(services);
        builder.AddTenantPropagation();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var validator = hostedServices.Single(s => s.GetType().Name == "TenantPropagationStartupValidator");

        // when
        var act = async () => await validator.StartAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }
}
