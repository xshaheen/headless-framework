// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tests.Surfaces;

public sealed class ApiSurfaceTelemetryTests : TestBase
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task should_enrich_completed_requests_without_surface_middleware(
        bool registerSurfaces,
        bool overrideEnrichment
    )
    {
        var completed = new ConcurrentDictionary<string, TaskCompletionSource<string?>>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Headless:StringEncryption:DefaultPassPhrase"] = "TestPassPhrase123456",
                ["Headless:StringEncryption:InitVectorBytes"] = "VGVzdElWMDEyMzQ1Njc4OQ==",
                ["Headless:StringEncryption:DefaultSalt"] = "VGVzdFNhbHQ=",
                ["Headless:StringHash:DefaultSalt"] = "TestSalt",
            }
        );
        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured = false;
            options.OpenTelemetry.ConfigureTracing = tracing => tracing.AddProcessor(new CaptureProcessor(completed));
            if (overrideEnrichment)
            {
                options.OpenTelemetry.ConfigureAspNetCoreInstrumentation = instrumentation =>
                {
                    instrumentation.EnrichWithHttpResponse = (activity, _) =>
                        activity.SetTag("headless.api.surface.name", "custom");
                };
            }
        });
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication();
        // Registration order must not affect the response enricher.
        if (registerSurfaces)
        {
            builder.Services.AddHeadlessApiSurfaces(options => options.AddSurface("Portal", _ => { }));
        }
        await using var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseRouting();
        app.Use(
            async (context, next) =>
            {
                Activity.Current?.SetTag("test.request", context.Request.Path.Value);
                if (context.Request.Path == "/denied")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next(context);
            }
        );
        app.MapGet("/portal", () => "ok").WithMetadata(new SurfaceMetadata("portal"));
        app.MapGet("/denied", () => "unreachable").WithMetadata(new SurfaceMetadata("portal"));
        app.MapGet("/plain", () => "ok");
        await app.StartAsync(AbortToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (
                var (path, tag, status) in new[]
                {
                    ("/portal", "Portal", 200),
                    ("/denied", "Portal", 403),
                    ("/plain", "unclassified", 200),
                    ("/missing", "unknown", 404),
                }
            )
            {
                var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                completed[path] = result;
                using var response = await client.GetAsync(path, AbortToken);
                ((int)response.StatusCode).Should().Be(status, await response.Content.ReadAsStringAsync(AbortToken));
                var actual = await result.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
                actual
                    .Should()
                    .Be(
                        overrideEnrichment ? "custom"
                        : registerSurfaces ? tag
                        : null
                    );
            }
        }
        finally
        {
            await app.StopAsync(AbortToken);
        }
    }

    private sealed class CaptureProcessor(ConcurrentDictionary<string, TaskCompletionSource<string?>> completed)
        : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            if (data.GetTagItem("test.request") is string path && completed.TryGetValue(path, out var result))
            {
                result.TrySetResult(data.GetTagItem("headless.api.surface.name")?.ToString());
            }
        }
    }

    private sealed record SurfaceMetadata(string SurfaceName) : IApiSurfaceMetadata;
}
