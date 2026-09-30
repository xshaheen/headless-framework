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
    public void should_tag_activity_with_default_attribute_name()
    {
        // given
        using var recorded = RecordedTestActivity.Start();

        // when
        TenantTelemetry.TagActivity(recorded.Activity, new TenantTelemetryOptions(), "acme");

        // then
        recorded.Activity.GetTagItem("tenant.id").Should().Be("acme");
    }

    [Fact]
    public void should_tag_activity_with_configured_attribute_name()
    {
        // given
        var options = new TenantTelemetryOptions { AttributeName = "app.tenant" };
        using var recorded = RecordedTestActivity.Start();

        // when
        TenantTelemetry.TagActivity(recorded.Activity, options, "acme");

        // then
        recorded.Activity.GetTagItem("app.tenant").Should().Be("acme");
        recorded.Activity.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public void should_not_tag_activity_when_traces_disabled()
    {
        // given
        var options = new TenantTelemetryOptions { EnrichTraces = false };
        using var recorded = RecordedTestActivity.Start();

        // when
        TenantTelemetry.TagActivity(recorded.Activity, options, "acme");

        // then
        recorded.Activity.GetTagItem("tenant.id").Should().BeNull();
    }

    [Fact]
    public void should_ignore_null_activity()
    {
        // when
        var act = () => TenantTelemetry.TagActivity(activity: null, new TenantTelemetryOptions(), "acme");

        // then
        act.Should().NotThrow();
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
        options.LogAttributeName.Should().Be(TenantTelemetryOptions.DefaultLogAttributeName);
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

    [Theory]
    [InlineData(" ", "tenant.id")]
    [InlineData("TenantId", "")]
    public void should_reject_blank_names(string logAttributeName, string attributeName)
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Telemetry(options =>
            {
                options.LogAttributeName = logAttributeName;
                options.AttributeName = attributeName;
            })
        );
        using var provider = builder.Services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<TenantTelemetryOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_blank_log_attribute_name_when_logs_disabled()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Telemetry(options =>
            {
                options.EnrichLogs = false;
                options.LogAttributeName = "";
            })
        );
        using var provider = builder.Services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TenantTelemetryOptions>>().Value;

        // then
        options.LogAttributeName.Should().BeEmpty();
    }
}
