// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace Tests.Setup;

public sealed class HeadlessServiceDefaultsOpenTelemetryOptionsTests
{
    // -------------------------------------------------------------------------
    // BuildSkipFunc – default paths
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public void should_skip_default_operational_paths_when_build_skip_func(string path)
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/health",
            alivePath: "/alive",
            healthMapped: true,
            aliveMapped: true
        );
        var context = _CreateContext(path);

        // then
        func(context).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/healthz")]
    [InlineData("/alive/extra")]
    [InlineData("/")]
    public void should_not_skip_non_operational_paths_when_build_skip_func(string path)
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/health",
            alivePath: "/alive",
            healthMapped: true,
            aliveMapped: true
        );
        var context = _CreateContext(path);

        // then
        func(context).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // BuildSkipFunc – mapped flags
    // -------------------------------------------------------------------------

    [Fact]
    public void should_not_skip_health_when_build_skip_func_health_not_mapped()
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/health",
            alivePath: "/alive",
            healthMapped: false,
            aliveMapped: true
        );
        var healthContext = _CreateContext("/health");
        var aliveContext = _CreateContext("/alive");

        // then
        func(healthContext).Should().BeFalse();
        func(aliveContext).Should().BeTrue();
    }

    [Fact]
    public void should_not_skip_alive_when_build_skip_func_alive_not_mapped()
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/health",
            alivePath: "/alive",
            healthMapped: true,
            aliveMapped: false
        );
        var healthContext = _CreateContext("/health");
        var aliveContext = _CreateContext("/alive");

        // then
        func(healthContext).Should().BeTrue();
        func(aliveContext).Should().BeFalse();
    }

    [Fact]
    public void should_not_skip_anything_when_build_skip_func_both_not_mapped()
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/health",
            alivePath: "/alive",
            healthMapped: false,
            aliveMapped: false
        );

        // then
        func(_CreateContext("/health")).Should().BeFalse();
        func(_CreateContext("/alive")).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // BuildSkipFunc – custom paths
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/ready")]
    public void should_skip_custom_operational_paths_when_build_skip_func(string path)
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/healthz",
            alivePath: "/ready",
            healthMapped: true,
            aliveMapped: true
        );
        var context = _CreateContext(path);

        // then
        func(context).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // BuildSkipFunc – empty path
    // -------------------------------------------------------------------------

    [Fact]
    public void should_return_false_when_build_skip_func_path_is_empty()
    {
        // given
        var func = HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
            healthPath: "/health",
            alivePath: "/alive",
            healthMapped: true,
            aliveMapped: true
        );

        // A context whose Request.Path is empty
        var context = new DefaultHttpContext { Request = { Path = PathString.Empty } };

        // then
        func(context).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // SkipOperationalEndpointFunc default value
    // -------------------------------------------------------------------------

    [Fact]
    public void default_skip_operational_endpoint_func_skips_default_health_and_alive_paths()
    {
        // given
        var options = new HeadlessServiceDefaultsOpenTelemetryOptions();

        // then
        options
            .SkipOperationalEndpointFunc(_CreateContext(HeadlessApiDefaultEndpointOptions.DefaultHealthPath))
            .Should()
            .BeTrue();

        options
            .SkipOperationalEndpointFunc(_CreateContext(HeadlessApiDefaultEndpointOptions.DefaultAlivePath))
            .Should()
            .BeTrue();

        options.SkipOperationalEndpointFunc(_CreateContext("/api/products")).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Filter vs SkipOperationalEndpointFunc interaction (via Setup.cs closure)
    // -------------------------------------------------------------------------

    [Fact]
    public void filter_property_set_to_custom_delegate_overrides_skip_func()
    {
        // The Setup.cs tracing lambda is:
        //   instrumentation.Filter = otel.Filter ?? (context => !otel.SkipOperationalEndpointFunc(context));
        // Simulate both branches here in isolation.

        var otel = new HeadlessServiceDefaultsOpenTelemetryOptions();

        // Branch 1: no custom Filter → skip func drives tracing
        var effectiveFilter = otel.Filter ?? (ctx => !otel.SkipOperationalEndpointFunc(ctx));
        effectiveFilter(_CreateContext("/health")).Should().BeFalse("health path should be filtered OUT of traces");
        effectiveFilter(_CreateContext("/api/users")).Should().BeTrue("regular path should be traced");

        // Branch 2: custom Filter → completely replaces skip func
        otel.Filter = _ => false; // never trace anything
        effectiveFilter = otel.Filter ?? (ctx => !otel.SkipOperationalEndpointFunc(ctx));
        effectiveFilter(_CreateContext("/api/users")).Should().BeFalse();
        effectiveFilter(_CreateContext("/health")).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static DefaultHttpContext _CreateContext(string path)
    {
        return new DefaultHttpContext { Request = { Path = path } };
    }
}
