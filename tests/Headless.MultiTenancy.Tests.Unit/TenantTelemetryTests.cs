// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TenantTelemetryTests : TestBase
{
    [Fact]
    public void should_tag_current_activity_and_open_log_scope_with_default_names()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantTelemetryTests>();
        using var recorded = RecordedTestActivity.Start();

        // when
        using (TenantTelemetry.Enrich(logger, new TenantTelemetryOptions(), "acme"))
        {
            // then
            logger.GetActiveScopeProperties().Should().ContainSingle().Which.Should().Be(_Pair("TenantId", "acme"));
        }

        recorded.Activity.GetTagItem("tenant.id").Should().Be("acme");
        logger.GetActiveScopeProperties().Should().BeEmpty();
    }

    [Fact]
    public void should_use_configured_names()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantTelemetryTests>();
        var options = new TenantTelemetryOptions { LogScopePropertyName = "Tenant", AttributeName = "app.tenant" };
        using var recorded = RecordedTestActivity.Start();

        // when
        using (TenantTelemetry.Enrich(logger, options, "acme"))
        {
            // then
            logger.GetActiveScopeProperties().Should().ContainSingle().Which.Should().Be(_Pair("Tenant", "acme"));
        }

        recorded.Activity.GetTagItem("app.tenant").Should().Be("acme");
        recorded.Activity.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public void should_skip_disabled_channels()
    {
        // given
        var logger = new ScopeRecordingLogger<TenantTelemetryTests>();
        var options = new TenantTelemetryOptions { EnrichLogs = false, EnrichTraces = false };
        using var recorded = RecordedTestActivity.Start();

        // when
        using var scope = TenantTelemetry.Enrich(logger, options, "acme");

        // then
        scope.Should().BeNull();
        logger.GetActiveScopeProperties().Should().BeEmpty();
        recorded.Activity.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public void should_open_no_scope_without_logger_or_activity()
    {
        // when
        var scope = TenantTelemetry.Enrich(logger: null, new TenantTelemetryOptions(), "acme");

        // then
        scope.Should().BeNull();
    }

    [Fact]
    public void should_return_metric_tag_for_ambient_tenant()
    {
        // given
        var currentTenant = new TestCurrentTenant { Id = "acme" };

        // when
        var found = TenantTelemetry.TryGetMetricTag(currentTenant, new TenantTelemetryOptions(), out var tag);

        // then
        found.Should().BeTrue();
        tag.Should().Be(_Pair("tenant.id", "acme"));
    }

    [Fact]
    public void should_return_no_metric_tag_without_tenant_or_when_disabled()
    {
        // given
        var noTenant = new TestCurrentTenant();
        var withTenant = new TestCurrentTenant { Id = "acme" };
        var disabled = new TenantTelemetryOptions { EnrichMetrics = false };

        // when / then
        TenantTelemetry.TryGetMetricTag(noTenant, new TenantTelemetryOptions(), out _).Should().BeFalse();
        TenantTelemetry.TryGetMetricTag(withTenant, disabled, out _).Should().BeFalse();
    }

    [Fact]
    public void should_resolve_default_options_without_any_registration()
    {
        // given
        using var provider = new ServiceCollection().AddOptions().BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TenantTelemetryOptions>>().Value;

        // then
        options.EnrichLogs.Should().BeTrue();
        options.EnrichTraces.Should().BeTrue();
        options.EnrichMetrics.Should().BeTrue();
        options.LogScopePropertyName.Should().Be(TenantTelemetryOptions.DefaultLogScopePropertyName);
        options.AttributeName.Should().Be(TenantTelemetryOptions.DefaultAttributeName);
    }

    [Fact]
    public void should_configure_options_through_tenancy_builder()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Telemetry(options => options.AttributeName = "app.tenant"));
        using var provider = builder.Services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<TenantTelemetryOptions>>().Value.AttributeName.Should().Be("app.tenant");
    }

    [Fact]
    public void should_reject_blank_name_for_enabled_channel()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Telemetry(options => options.LogScopePropertyName = " "));
        using var provider = builder.Services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<TenantTelemetryOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_blank_name_for_disabled_channel()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Telemetry(options =>
            {
                options.EnrichTraces = false;
                options.EnrichMetrics = false;
                options.AttributeName = "";
            })
        );
        using var provider = builder.Services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TenantTelemetryOptions>>().Value;

        // then
        options.AttributeName.Should().BeEmpty();
    }

    private static KeyValuePair<string, object?> _Pair(string key, string value)
    {
        return new(key, value);
    }
}
