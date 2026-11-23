// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
using Tests.Helpers;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Tests;

/// <summary>
/// An <c>AddHeadless</c> host, with its native logging profile and the service-defaults OpenTelemetry pipeline, hands
/// every <see cref="ILogger"/> record to the OpenTelemetry log exporter once, redacted.
/// </summary>
public sealed partial class OpenTelemetryLogExportTests : TestBase
{
    private const string _LogMessage = "OpenTelemetry export probe";

    [Fact]
    public async Task should_export_a_log_written_under_add_headless_to_open_telemetry_once()
    {
        // given
        var logs = new CapturedItems<CapturedLog>();
        await using var app = await _CreateAppAsync(logs);
        var logger = app.Services.GetRequiredService<ILogger<OpenTelemetryLogExportTests>>();

        // when
        LogProbe(logger, 42);

        // then: the processor runs inline, so every copy the pipeline exports has arrived when the call returns
        var record = await _WaitForLogAsync(logs);
        record.Attributes.Should().ContainSingle(a => a.Key == "OrderId").Which.Value.Should().Be(42);
        logs.Count(log => log.Message?.StartsWith(_LogMessage, StringComparison.Ordinal) == true).Should().Be(1);
    }

    [Fact]
    public async Task should_export_classified_member_redacted_when_redaction_is_registered()
    {
        // given
        var logs = new CapturedItems<CapturedLog>();
        await using var app = await _CreateAppAsync(
            logs,
            services =>
                services.AddRedaction(redaction =>
                    redaction.SetRedactor<StarRedactor>(new DataClassificationSet(_Personal))
                )
        );
        var logger = app.Services.GetRequiredService<ILogger<OpenTelemetryLogExportTests>>();

        // when
#pragma warning disable CA1848, CA1873 // The probe needs a destructured {@Customer} hole, which [LoggerMessage] cannot express.
        logger.LogInformation(_LogMessage + " for {@Customer}", new Customer("Alice", "Gold"));
#pragma warning restore CA1848, CA1873

        // then: logging keeps the capture's "@" in the attribute name
        var record = await _WaitForLogAsync(logs);
        var attribute = record.Attributes.Should().ContainSingle(a => a.Key == "@Customer").Subject;
        attribute.Value.Should().Be($"Customer {{ Name = {StarRedactor.Output}, Tier = Gold }}");
        record.Message.Should().NotContain("Alice");
    }

    [Fact]
    public async Task should_export_a_classified_logger_message_parameter_redacted()
    {
        // given
        var logs = new CapturedItems<CapturedLog>();
        await using var app = await _CreateAppAsync(
            logs,
            services =>
                services.AddRedaction(redaction =>
                    redaction.SetRedactor<StarRedactor>(new DataClassificationSet(_Personal))
                )
        );
        var logger = app.Services.GetRequiredService<ILogger<OpenTelemetryLogExportTests>>();

        // when
        LogClassifiedProbe(logger, "alice@example.com", 42);

        // then: the parameter reaches the exporter redacted, and the rendered text carries it instead of a {Email} hole
        var record = await _WaitForLogAsync(logs);
        record.Attributes.Should().Contain(a => a.Key == "Email" && Equals(a.Value, StarRedactor.Output));
        record.Message.Should().Contain(StarRedactor.Output).And.NotContain("{Email}").And.NotContain("alice");
        record.Attributes.Select(a => a.Value?.ToString()).Should().NotContain("alice@example.com");
    }

    private Task<WebApplication> _CreateAppAsync(CapturedItems<CapturedLog> logs) => _CreateAppAsync(logs, _ => { });

    private async Task<WebApplication> _CreateAppAsync(
        CapturedItems<CapturedLog> logs,
        Action<IServiceCollection> configureServices
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test }
        );
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // AddHeadless adds its own console; the default providers would print every entry twice.
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

        var app = builder.Build();
        await app.StartAsync(AbortToken);

        return app;
    }

    private static Task<CapturedLog> _WaitForLogAsync(CapturedItems<CapturedLog> logs) =>
        logs.WaitForAsync(log => log.Message?.StartsWith(_LogMessage, StringComparison.Ordinal) == true);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = _LogMessage + " for {OrderId}")]
    private static partial void LogProbe(ILogger logger, int orderId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = _LogMessage + " from {Email} for {OrderId}")]
    private static partial void LogClassifiedProbe(ILogger logger, [PersonalData] string email, int orderId);

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
    private sealed class LogCaptureProcessor(CapturedItems<CapturedLog> logs) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            logs.Add(new CapturedLog(data.FormattedMessage, [.. data.Attributes ?? []]));
        }
    }
}
