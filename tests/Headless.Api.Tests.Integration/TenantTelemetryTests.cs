// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
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
/// End to end through the service-defaults OpenTelemetry pipeline, with no telemetry-specific registration: log records
/// carry the tenant that is ambient when they are written, the request span carries the resolved tenant, and spans
/// started under a tenant carry it too.
/// </summary>
public sealed partial class TenantTelemetryTests : TestBase
{
    private const string _EndpointLogMessage = "Tenant telemetry endpoint served";
    private const string _NestedLogMessage = "Tenant telemetry nested work";
    private const string _ChildSpanName = "tenant-telemetry-child";

    // Matches the ServiceDefaults "Headless.*" wildcard source.
    private static readonly ActivitySource _Source = new("Headless.Tests.TenantTelemetry");

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
        record.Attributes.Should().ContainSingle(a => a.Key == "TenantId").Which.Value.Should().Be("TENANT-1");

        var span = await capture.WaitForRequestSpanAsync(AbortToken);
        span.GetTagItem("tenant.id").Should().Be("TENANT-1");

        var child = await capture.WaitForSpanAsync(_ChildSpanName, AbortToken);
        child.GetTagItem("tenant.id").Should().Be("TENANT-1");
    }

    [Fact]
    public async Task should_label_logs_with_the_tenant_ambient_when_written()
    {
        // given
        var capture = new TelemetryCapture();
        await using var app = await _CreateAppAsync(capture);
        using var client = HttpTenancyTestHarness.CreateClient(app);

        // when
        await _GetAsync(client, "TENANT-1", nestedTenantId: "TENANT-2");

        // then
        var nested = await capture.WaitForLogAsync(_NestedLogMessage, AbortToken);
        nested.Attributes.Should().ContainSingle(a => a.Key == "TenantId").Which.Value.Should().Be("TENANT-2");

        var outer = await capture.WaitForLogAsync(_EndpointLogMessage, AbortToken);
        outer.Attributes.Should().ContainSingle(a => a.Key == "TenantId").Which.Value.Should().Be("TENANT-1");
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
        // Only the template's own null placeholder value; the processor adds nothing without a tenant.
        record.Attributes.Should().ContainSingle(a => a.Key == "TenantId").Which.Value.Should().BeNull();

        var span = await capture.WaitForRequestSpanAsync(AbortToken);
        span.GetTagItem("tenant.id").Should().BeNull();

        var child = await capture.WaitForSpanAsync(_ChildSpanName, AbortToken);
        child.GetTagItem("tenant.id").Should().BeNull();
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
            (string? nested, ICurrentTenant currentTenant, ILogger<TenantTelemetryTests> logger) =>
            {
                using (_Source.StartActivity(_ChildSpanName))
                {
                    LogEndpointServed(logger, currentTenant.Id);
                }

                if (nested is not null)
                {
                    using (currentTenant.Change(nested))
                    {
                        LogNestedWork(logger);
                    }
                }

                return Results.Ok(currentTenant.Id);
            }
        );

        await app.StartAsync(AbortToken);

        return app;
    }

    private async Task _GetAsync(HttpClient client, string? tenantId, string? nestedTenantId = null)
    {
        using var request = HttpTenancyTestHarness.CreateRequest(
            HttpMethod.Get,
            nestedTenantId is null ? "/tenant-telemetry" : $"/tenant-telemetry?nested={nestedTenantId}",
            user: "alice",
            tenantId
        );
        using var response = await client.SendAsync(request, AbortToken);
        response.EnsureSuccessStatusCode();
    }

    // The {TenantId} placeholder already puts a TenantId attribute on the record; the processor must not add a second.
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = _EndpointLogMessage + " for {TenantId}")]
    private static partial void LogEndpointServed(ILogger logger, string? tenantId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = _NestedLogMessage)]
    private static partial void LogNestedWork(ILogger logger);

    private sealed record CapturedLog(string? Message, IReadOnlyList<KeyValuePair<string, object?>> Attributes);

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
                log => log.Message?.StartsWith(message, StringComparison.Ordinal) == true,
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

        public Task<Activity> WaitForSpanAsync(string name, CancellationToken cancellationToken)
        {
            return _WaitForAsync(
                _spans,
                span => string.Equals(span.OperationName, name, StringComparison.Ordinal),
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
            logs.Enqueue(new CapturedLog(data.FormattedMessage, [.. data.Attributes ?? []]));
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
