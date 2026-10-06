// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.MultiTenancy;
using Headless.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class SetupApiTests
{
    [Fact]
    public void should_register_current_tenant_by_default_when_add_headless_api()
    {
        // given
        var builder = WebApplication.CreateBuilder();

        // when
        builder.AddHeadless();

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        currentTenant.Id.Should().BeNull();
        currentTenant.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void should_replace_null_current_tenant_fallback_when_add_headless_api()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        builder.AddHeadless();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
    }

    [Fact]
    public void should_preserve_custom_current_tenant_when_add_headless_api()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        var customTenant = new ApiCustomCurrentTenant();
        builder.Services.AddSingleton<ICurrentTenant>(customTenant);

        // when
        builder.AddHeadless();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(customTenant);
    }

    [Fact]
    public async Task should_register_service_defaults_when_add_headless_api()
    {
        // given
        var builder = WebApplication.CreateBuilder();

        // when
        builder.AddHeadless();

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var kestrelOptions = serviceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
        var statusCodesRewriter = serviceProvider.GetRequiredService<StatusCodesRewriterMiddleware>();
        var healthReport = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("live"),
            CancellationToken.None
        );

        // then
        kestrelOptions.AddServerHeader.Should().BeFalse();
        kestrelOptions.Limits.MaxRequestBodySize.Should().Be(1024 * 1024 * 30);
        kestrelOptions.Limits.MaxRequestHeaderCount.Should().Be(40);
        statusCodesRewriter.Should().NotBeNull();
        healthReport.Entries.Should().ContainKey("self");
        healthReport.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void should_register_no_string_encryption_or_lookup_hasher_when_add_headless_without_security_configuration()
    {
        // given
        var builder = WebApplication.CreateBuilder();

        // when
        builder.AddHeadless();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetService<IStringEncryptionService>().Should().BeNull();
        serviceProvider.GetService<ILookupHasher>().Should().BeNull();
    }

    [Fact]
    public async Task should_register_convention_defaults_when_add_headless()
    {
        // given
        var builder = WebApplication.CreateBuilder();

        // when
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
        });

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<HeadlessServiceDefaultsOptions>();
        var kestrelOptions = serviceProvider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
        var healthReport = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("live"),
            CancellationToken.None
        );

        // then
        options.OpenApi.Enabled.Should().BeTrue();
        options.Validation.RequireUseHeadless.Should().BeTrue();
        options.Validation.RequireMapHeadlessEndpoints.Should().BeTrue();
        options.HttpClient.UseServiceDiscovery.Should().BeTrue();
        kestrelOptions.AddServerHeader.Should().BeFalse();
        options.Antiforgery.Enabled.Should().BeFalse();
        serviceProvider.GetService<IAntiforgery>().Should().BeNull("antiforgery is opt-in by default");
        serviceProvider.GetRequiredService<IProblemDetailsCreator>().Should().NotBeNull();
        builder.Services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IStartupFilter));
        healthReport.Entries.Should().ContainKey("self");
        healthReport.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void should_register_antiforgery_when_add_headless_explicitly_enabled()
    {
        // given
        var builder = WebApplication.CreateBuilder();

        // when
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.OpenTelemetry.Enabled = false;
            options.Antiforgery.Enabled = true;
        });

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<IAntiforgery>().Should().NotBeNull();
    }

    [Fact]
    public void should_replace_null_current_tenant_and_store_custom_claim_type_when_add_headless_multi_tenancy()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        builder.AddHeadlessMultiTenancy(options => options.ClaimType = "custom_tenant_id");

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();
        var options = serviceProvider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        options.ClaimType.Should().Be("custom_tenant_id");
    }

    [Fact]
    public void should_preserve_custom_current_tenant_when_add_headless_multi_tenancy()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var customTenant = new ApiCustomCurrentTenant();
        builder.Services.AddSingleton<ICurrentTenant>(customTenant);

        // when
        builder.AddHeadlessMultiTenancy();

        using var serviceProvider = builder.Services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ICurrentTenant>().Should().BeSameAs(customTenant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fall_back_to_default_claim_type_when_add_headless_multi_tenancy_blank(string blankClaimType)
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessMultiTenancy(options => options.ClaimType = blankClaimType);

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MultiTenancyOptions>>().Value;

        // then
        options.ClaimType.Should().Be(UserClaimTypes.TenantId);
    }

    [Fact]
    public void should_apply_only_once_when_configure_global_settings()
    {
        // Reset guard so we start from a clean slate.
        SetupApi.ResetForTesting();

        // First call should apply (returns without skipping).
        SetupApi.ConfigureGlobalSettings();
        var cascadeAfterFirst = ValidatorOptions.Global.DefaultRuleLevelCascadeMode;

        // Mutate a setting that ConfigureGlobalSettings would override.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Continue;

        // Second call must be a no-op.
        SetupApi.ConfigureGlobalSettings();

        ValidatorOptions
            .Global.DefaultRuleLevelCascadeMode.Should()
            .Be(CascadeMode.Continue, "second call must not overwrite settings");

        // Restore for other tests.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = cascadeAfterFirst;
        SetupApi.ResetForTesting();
    }

    [Fact]
    public void should_allow_configure_global_settings_to_reapply_when_reset_for_testing()
    {
        // Ensure guard is set (call once or it may already be set from test isolation).
        SetupApi.ConfigureGlobalSettings();

        // Mutate.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Continue;

        // After reset, ConfigureGlobalSettings should run again.
        SetupApi.ResetForTesting();
        SetupApi.ConfigureGlobalSettings();

        ValidatorOptions
            .Global.DefaultRuleLevelCascadeMode.Should()
            .Be(CascadeMode.Stop, "ConfigureGlobalSettings should re-apply Stop after reset");

        // Leave in a consistent state.
        SetupApi.ResetForTesting();
    }

    private sealed class ApiCustomCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => true;

        public string Id => "custom";

        public string Name => "Custom";

        public IDisposable Change(string? id, string? name = null)
        {
            return new ApiCurrentTenantScope();
        }
    }

    private sealed class ApiCurrentTenantScope : IDisposable
    {
        public void Dispose() { }
    }
}
