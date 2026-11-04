// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Api.ServiceDefaults;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace Tests.Setup;

public sealed class ServiceDefaultsTracingSourceTests : TestBase
{
    [Fact]
    public async Task should_trace_the_application_sources_named_under_the_application_name()
    {
        // given
        var builder = WebApplication.CreateBuilder();
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenApi.Enabled = false;
            options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured = false;
        });
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication();
        await using var app = builder.Build();
        _ = app.Services.GetRequiredService<TracerProvider>();
        var applicationName = builder.Environment.ApplicationName;

        // when
        using var feature = new ActivitySource(applicationName + ".Orders");
        using var root = new ActivitySource(applicationName);
        using var unrelated = new ActivitySource("Unrelated." + applicationName);

        // then
        feature.HasListeners().Should().BeTrue();
        root.HasListeners().Should().BeTrue();
        unrelated.HasListeners().Should().BeFalse();
    }
}
