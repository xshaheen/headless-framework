// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.Api;
using Headless.Api.ServiceDefaults;
using Headless.Constants;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// End to end through the service-defaults OpenTelemetry pipeline: a request whose tenant the claim middleware
/// resolves produces a log record carrying the <c>TenantId</c> scope attribute and a request span carrying
/// <c>tenant.id</c>, with no telemetry-specific registration.
/// </summary>
public sealed partial class TenantTelemetryTests : TestBase
{
    private const string _EndpointLogMessage = "Tenant telemetry endpoint served";

    [Fact]
    public async Task should_export_log_record_and_request_span_with_resolved_tenant()
    {
        // given
        var capture = new TelemetryCapture();
        await using var app = await _CreateAppAsync(capture);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        await _GetAsync(client, "TENANT-1");

        // then
        var record = await capture.WaitForLogAsync(_EndpointLogMessage, AbortToken);
        record.ScopeAttributes.Should().Contain(new KeyValuePair<string, object?>("TenantId", "TENANT-1"));

        var span = await capture.WaitForRequestSpanAsync(AbortToken);
        span.GetTagItem("tenant.id").Should().Be("TENANT-1");
    }

    [Fact]
    public async Task should_export_request_without_tenant_attributes_when_no_tenant_resolves()
    {
        // given
        var capture = new TelemetryCapture();
        await using var app = await _CreateAppAsync(capture);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        await _GetAsync(client, tenantId: null);

        // then
        var record = await capture.WaitForLogAsync(_EndpointLogMessage, AbortToken);
        record.ScopeAttributes.Should().NotContain(attribute => attribute.Key == "TenantId");

        var span = await capture.WaitForRequestSpanAsync(AbortToken);
        span.GetTagItem("tenant.id").Should().BeNull();
    }

    private async Task<WebApplication> _CreateAppAsync(TelemetryCapture capture)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        HttpTenancyTestHarness.AddDefaultHeadlessSecurityConfiguration(builder.Configuration);

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenApi.Enabled = false;
            options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured = false;
            options.OpenTelemetry.ConfigureLogging = logging => logging.AddProcessor(capture.LogProcessor);
            options.OpenTelemetry.ConfigureTracing = tracing => tracing.AddProcessor(capture.SpanProcessor);
        });

        builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()));
        builder.Services.AddTestAuthentication();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseHeadlessTenancy();
        app.UseAuthorization();

        app.MapGet(
            "/tenant-telemetry",
            (ICurrentTenant currentTenant, ILogger<TenantTelemetryTests> logger) =>
            {
                LogEndpointServed(logger);
                return Results.Ok(currentTenant.Id);
            }
        );

        await app.StartAsync(AbortToken);

        return app;
    }

    private async Task _GetAsync(HttpClient client, string? tenantId)
    {
        using var request = HttpTenancyTestHarness.CreateRequest(
            HttpMethod.Get,
            "/tenant-telemetry",
            user: "alice",
            tenantId
        );
        using var response = await client.SendAsync(request, AbortToken);
        response.EnsureSuccessStatusCode();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = _EndpointLogMessage)]
    private static partial void LogEndpointServed(ILogger logger);

    private sealed record CapturedLog(string? Message, IReadOnlyList<KeyValuePair<string, object?>> ScopeAttributes);

    /// <summary>
    /// Copies what the assertions need when each record or span ends: the SDK pools <see cref="LogRecord"/>
    /// instances, so a record must not be read after its processor call returns.
    /// </summary>
    private sealed class TelemetryCapture
    {
        private readonly ConcurrentQueue<CapturedLog> _logs = new();
        private readonly ConcurrentQueue<Activity> _spans = new();

        public TelemetryCapture()
        {
            LogProcessor = new LogCaptureProcessor(_logs);
            SpanProcessor = new SpanCaptureProcessor(_spans);
        }

        public BaseProcessor<LogRecord> LogProcessor { get; }

        public BaseProcessor<Activity> SpanProcessor { get; }

        public Task<CapturedLog> WaitForLogAsync(string message, CancellationToken cancellationToken)
        {
            return _WaitForAsync(
                _logs,
                log => string.Equals(log.Message, message, StringComparison.Ordinal),
                cancellationToken
            );
        }

        public Task<Activity> WaitForRequestSpanAsync(CancellationToken cancellationToken)
        {
            // The ASP.NET Core server span stops after the response is flushed, so it can end after the client
            // has already read the response.
            return _WaitForAsync(
                _spans,
                span =>
                    span.Kind == ActivityKind.Server
                    && string.Equals(
                        span.GetTagItem("url.path") as string,
                        "/tenant-telemetry",
                        StringComparison.Ordinal
                    ),
                cancellationToken
            );
        }

        private static async Task<T> _WaitForAsync<T>(
            ConcurrentQueue<T> items,
            Func<T, bool> predicate,
            CancellationToken cancellationToken
        )
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            while (true)
            {
                foreach (var item in items)
                {
                    if (predicate(item))
                    {
                        return item;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }
    }

    private sealed class LogCaptureProcessor(ConcurrentQueue<CapturedLog> logs) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            var scopeAttributes = new List<KeyValuePair<string, object?>>();
            data.ForEachScope(
                static (scope, attributes) =>
                {
                    foreach (var attribute in scope)
                    {
                        attributes.Add(attribute);
                    }
                },
                scopeAttributes
            );

            logs.Enqueue(new CapturedLog(data.FormattedMessage, scopeAttributes));
        }
    }

    private sealed class SpanCaptureProcessor(ConcurrentQueue<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            spans.Enqueue(data);
        }
    }
}
