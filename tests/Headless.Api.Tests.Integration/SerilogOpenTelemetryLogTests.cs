// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless;
using Headless.Api.ServiceDefaults;
using Headless.Logging;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
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

    [Fact]
    public async Task should_export_classified_member_redacted_when_redaction_is_registered()
    {
        // given
        var logs = new ConcurrentQueue<CapturedLog>();
        await using var app = await _CreateAppAsync(
            logs,
            services =>
                services.AddRedaction(redaction =>
                    redaction.SetRedactor<StarRedactor>(new DataClassificationSet(_Personal))
                )
        );
        var logger = app.Services.GetRequiredService<ILogger<SerilogOpenTelemetryLogTests>>();

        // when
#pragma warning disable CA1848, CA1873 // The probe needs a destructured {@Customer} hole, which [LoggerMessage] cannot express.
        logger.LogInformation(_LogMessage + " for {@Customer}", new Customer("Alice", "Gold"));
#pragma warning restore CA1848, CA1873

        // then
        var record = await _WaitForLogAsync(logs, AbortToken);
        var attribute = record.Attributes.Should().ContainSingle(a => a.Key == "Customer").Subject;
        var customer = attribute.Value?.ToString();
        customer.Should().Contain(StarRedactor.Output).And.Contain("Gold").And.NotContain("Alice");
    }

    private Task<WebApplication> _CreateAppAsync(ConcurrentQueue<CapturedLog> logs) => _CreateAppAsync(logs, _ => { });

    private async Task<WebApplication> _CreateAppAsync(
        ConcurrentQueue<CapturedLog> logs,
        Action<IServiceCollection> configureServices
    )
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

        configureServices(builder.Services);

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

    private static DataClassification _Personal => new("Tests", "Personal");

    private sealed class PersonalDataAttribute() : DataClassificationAttribute(_Personal);

    public sealed record Customer([property: PersonalData] string Name, string Tier);

    private sealed class StarRedactor : Redactor
    {
        public const string Output = "*****";

        public override int GetRedactedLength(ReadOnlySpan<char> input) => Output.Length;

        public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
        {
            Output.AsSpan().CopyTo(destination);
            return Output.Length;
        }
    }

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
