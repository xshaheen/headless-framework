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
/// Tests covering <see cref="SetupMessagingTenancy"/> messaging tenancy registration through the root
/// <c>AddHeadlessTenancy</c> surface — startup-time validation that fails fast when only the
/// framework's fallback <see cref="NullCurrentTenant"/> is registered.
/// </summary>
public sealed class SetupMessagingTenancyTests : TestBase
{
    [Fact]
    public void should_register_tenant_propagation_filters_from_headless_tenancy_root()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));

        // then
        builder
            .Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IConsumeFilter)
                && descriptor.ImplementationType == typeof(TenantPropagationConsumeFilter)
            )
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
        builder
            .Services.Where(descriptor =>
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
        seam!.Status.Should().Be(TenantPostureStatus.Propagating);
        seam.Capabilities.Should().BeEquivalentTo("propagate-tenant");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_enable_strict_publish_from_headless_tenancy_root_regardless_of_messaging_setup_order(
        bool addMessagingBeforeTenancy
    )
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(Substitute.For<ICurrentTenant>());

        // when
        if (addMessagingBeforeTenancy)
        {
            builder.Services.AddHeadlessMessaging(_ => { });
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
            );
        }
        else
        {
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
            );
            builder.Services.AddHeadlessMessaging(_ => { });
        }

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;

        // then
        options.TenantContextRequired.Should().BeTrue();

        var manifest = builder.Services.GetOrAddTenantPostureManifest();
        var seam = manifest.GetSeam("Messaging");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should().BeEquivalentTo("propagate-tenant", "require-tenant-on-publish");
    }

    [Fact]
    public void should_not_duplicate_tenant_propagation_filters_when_root_called_twice()
    {
        // given
        var rootBuilder = Host.CreateApplicationBuilder();

        // when — repeated root configuration must not double-register either filter type.
        rootBuilder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));
        rootBuilder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));

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
                descriptor.ServiceType == typeof(IHeadlessTenancyValidator)
                && descriptor.ImplementationType?.Name == "TenantPropagationStartupValidator"
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task should_emit_propagation_error_diagnostic_when_no_other_tenant_seam_or_override_is_registered()
    {
        // given — only Messaging propagation is wired: no HTTP / EF / Mediator seam contributes,
        // and the consumer did not supply an ICurrentTenant override. The default CurrentTenant
        // is registered (AsyncLocal-backed) but no one populates the accessor.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));

        await using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
        var validator = provider
            .GetServices<IHeadlessTenancyValidator>()
            .Single(v => v.GetType().Name == "TenantPropagationStartupValidator");
        var manifest = provider.GetRequiredService<TenantPostureManifest>();
        var context = new HeadlessTenancyValidationContext(provider, manifest);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle();
        diagnostics[0].Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT");
        diagnostics[0].Seam.Should().Be(HeadlessMessagingTenancyBuilder.Seam);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task should_emit_no_propagation_diagnostic_when_consumer_supplied_ICurrentTenant_is_registered()
    {
        // given — a consumer-supplied (non-CurrentTenant, non-NullCurrentTenant) ICurrentTenant
        // is registered before tenancy root configures messaging propagation. The validator should
        // recognize the override as a real tenant source and emit no diagnostic.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(Substitute.For<ICurrentTenant>());
        builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(messaging => messaging.PropagateTenant()));

        await using var provider = builder.Services.BuildServiceProvider();
        var validator = provider
            .GetServices<IHeadlessTenancyValidator>()
            .Single(v => v.GetType().Name == "TenantPropagationStartupValidator");
        var manifest = provider.GetRequiredService<TenantPostureManifest>();
        var context = new HeadlessTenancyValidationContext(provider, manifest);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().BeEmpty();
        await Task.CompletedTask;
    }
}
