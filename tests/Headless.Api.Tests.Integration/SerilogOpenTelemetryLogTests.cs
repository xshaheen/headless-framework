// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless;
using Headless.Api.ServiceDefaults;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Tests;

/// <summary>
/// A host that logs through Serilog the way <c>docs/llms/logging.md</c> wires it, next to the service-defaults
/// OpenTelemetry pipeline, still hands every <see cref="ILogger"/> record to the OpenTelemetry log exporter.
/// </summary>
public sealed partial class SerilogOpenTelemetryLogTests : TestBase
{
    private const string _LogMessage = "Serilog to OpenTelemetry probe";

    [Fact]
    public async Task should_export_log_written_under_serilog_to_open_telemetry()
    {
        // given
        var logs = new ConcurrentQueue<CapturedLog>();
        await using var app = await _CreateAppAsync(logs);
        var logger = app.Services.GetRequiredService<ILogger<SerilogOpenTelemetryLogTests>>();

        // when
        LogProbe(logger, 42);

        // then
        var record = await _WaitForLogAsync(logs, AbortToken);
        record.Attributes.Should().ContainSingle(a => a.Key == "OrderId").Which.Value.Should().Be(42);
    }

    private async Task<WebApplication> _CreateAppAsync(ConcurrentQueue<CapturedLog> logs)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // The documented setup: Serilog owns the console, so the default providers go before AddHeadless adds
        // the OpenTelemetry provider, and writeToProviders hands every event on to that provider.
        builder.Logging.ClearProviders();

        builder.AddHeadless(configureServices: options =>
        {
            options.Validation.ValidateServiceProviderOnStartup = false;
            options.Validation.RequireUseHeadless = false;
            options.Validation.RequireMapHeadlessEndpoints = false;
            options.Validation.RequireStatusCodesRewriter = false;
            options.OpenApi.Enabled = false;
            options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured = false;
            options.OpenTelemetry.ConfigureLogging = logging => logging.AddProcessor(new LogCaptureProcessor(logs));
        });

        // Files off so the test leaves nothing on disk.
        builder.Host.UseSerilog(
            (context, services, configuration) =>
                configuration.ConfigureReloadableLoggerConfiguration(
                    services,
                    context.Configuration,
                    context.HostingEnvironment,
                    new SerilogOptions { WriteToFiles = false }
                ),
            writeToProviders: true
        );

        var app = builder.Build();
        await app.StartAsync(AbortToken);

        return app;
    }

    private static async Task<CapturedLog> _WaitForLogAsync(
        ConcurrentQueue<CapturedLog> logs,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            foreach (var log in logs)
            {
                if (log.Message?.StartsWith(_LogMessage, StringComparison.Ordinal) == true)
                {
                    return log;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = _LogMessage + " for {OrderId}")]
    private static partial void LogProbe(ILogger logger, int orderId);

    private sealed record CapturedLog(string? Message, IReadOnlyList<KeyValuePair<string, object?>> Attributes);

    // The SDK pools LogRecord instances, so copy what the assertions read before OnEnd returns.
    private sealed class LogCaptureProcessor(ConcurrentQueue<CapturedLog> logs) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            logs.Enqueue(new CapturedLog(data.FormattedMessage, [.. data.Attributes ?? []]));
        }
    }
}
