// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Logging;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

namespace Tests;

/// <summary>A logging pipeline a consumer can build from this framework.</summary>
public enum LogPipeline
{
    /// <summary>Microsoft.Extensions.Logging's own factory, the OpenTelemetry provider, and Microsoft's JSON console.</summary>
    MelOtel,

    /// <summary>
    /// <see cref="MelOtel"/> with Microsoft's <c>EnableRedaction()</c> factory and <c>AddRedaction</c> mapping the
    /// Headless taxonomy to erasing: classification at the source, no Headless wrapper, no sink masking.
    /// </summary>
    MelOtelMicrosoftRedaction,

    /// <summary>
    /// An injected <see cref="Microsoft.Extensions.Logging.ILogger"/> under Serilog's factory, forwarding every event to
    /// the OpenTelemetry provider. With redaction on this is <c>AddHeadlessSerilog</c>'s profile.
    /// </summary>
    MelSerilog,

    /// <summary>The same host as <see cref="MelSerilog"/>, written through the injected <see cref="Serilog.ILogger"/>.</summary>
    NativeSerilog,

    /// <summary>
    /// <see cref="MelOtel"/> with Serilog attached as one more <see cref="ILoggerProvider"/> (a sink), not as the
    /// logger factory: Microsoft.Extensions.Logging's factory, and so the Headless redaction, stays in charge of every
    /// provider. With redaction on, Serilog also gets the classification destructuring policy.
    /// </summary>
    MelOtelSerilogSink,

    /// <summary>
    /// <c>AddHeadlessLogging</c>: Microsoft.Extensions.Logging with the Headless console profile and enrichment, and the
    /// OpenTelemetry provider. With redaction on it adds the recommended classification and text pattern.
    /// </summary>
    HeadlessLogging,
}

/// <summary>Whether the pipeline runs the documented Headless redaction.</summary>
public enum Redaction
{
    /// <summary>Nothing registered: the pipeline as its library ships it.</summary>
    Off,

    /// <summary>
    /// <c>AddHeadlessRedaction</c> with a national-ID text pattern and <c>ClassifyProperty("Email", Private)</c>, the
    /// configuration <c>docs/llms/logging.md</c> recommends. For <see cref="LogPipeline.MelOtelMicrosoftRedaction"/> it
    /// means Microsoft's classification redaction only.
    /// </summary>
    On,
}

/// <summary>Builds one pipeline, takes what a scenario logs, and returns the bytes every sink received.</summary>
internal sealed class LogLeakHost : IDisposable
{
    private readonly IHost _host;
    private readonly StringWriter _console;
    private readonly StringWriter _consoleText;
    private readonly OtlpCapture _otlp;
    private readonly string? _logDirectory;
    private bool _disposed;

    private LogLeakHost(
        IHost host,
        StringWriter console,
        StringWriter consoleText,
        OtlpCapture otlp,
        string? logDirectory
    )
    {
        _host = host;
        _console = console;
        _consoleText = consoleText;
        _otlp = otlp;
        _logDirectory = logDirectory;
    }

    public Microsoft.Extensions.Logging.ILogger Logger =>
        _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Leak.Orders");

    public Serilog.ILogger SerilogLogger =>
        _host
            .Services.GetRequiredService<Serilog.ILogger>()
            .ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Leak.Orders");

    public static LogLeakHost Start(LogPipeline pipeline, Redaction redaction)
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production }
        );
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var console = new StringWriter(CultureInfo.InvariantCulture);
        var consoleText = new StringWriter(CultureInfo.InvariantCulture);
        var otlp = new OtlpCapture();
        string? logDirectory = null;

        // The Headless service defaults: the rendered message and the scopes travel with every exported record.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.AddOtlpExporter(
                (exporter, processor) =>
                {
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Endpoint = otlp.Endpoint("logs");
                    exporter.HttpClientFactory = otlp.CreateClient;
                    processor.ExportProcessorType = ExportProcessorType.Simple;
                }
            );
        });

        if (pipeline is LogPipeline.HeadlessLogging)
        {
            builder.AddHeadlessLogging(new HeadlessLoggingOptions { ConsoleFormat = HeadlessConsoleFormat.Json });
            _CaptureJsonConsole(builder, console);
        }

        if (pipeline is LogPipeline.MelOtel or LogPipeline.MelOtelMicrosoftRedaction)
        {
            _AddJsonConsoleCapture(builder, console);
        }

        if (pipeline is LogPipeline.MelOtelMicrosoftRedaction && redaction == Redaction.On)
        {
            builder.Logging.EnableRedaction();
            builder.Services.AddRedaction(redact =>
            {
                redact.SetRedactor<ErasingRedactor>(
                    new DataClassificationSet(HeadlessDataClassifications.Secret),
                    new DataClassificationSet(HeadlessDataClassifications.Private),
                    new DataClassificationSet(HeadlessDataClassifications.Identifier)
                );
            });
        }

        if (pipeline is LogPipeline.MelOtelSerilogSink)
        {
            logDirectory = Path.Combine(Path.GetTempPath(), "headless-leak-" + Guid.NewGuid().ToString("N"));
            var directory = logDirectory;

            // The redaction registered below wraps the factory, so this provider receives entries already redacted.
            builder.Services.AddSingleton<ILoggerProvider>(provider =>
            {
                var configuration = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext();

                if (provider.GetService<IRedactorProvider>() is { } redactorProvider)
                {
                    configuration.Destructure.WithRedaction(redactorProvider);
                }

                _AddConsoleCaptures(configuration, console, consoleText);
                configuration.WriteTo.File(
                    new MessageTemplateTextFormatter(SerilogFactory.OutputTemplate, CultureInfo.InvariantCulture),
                    Path.Combine(directory, "warning-.log"),
                    rollingInterval: RollingInterval.Day
                );

                return new SerilogLoggerProvider(configuration.CreateLogger(), dispose: true);
            });
        }

        if (
            pipeline is LogPipeline.MelOtel or LogPipeline.MelOtelSerilogSink or LogPipeline.HeadlessLogging
            && redaction == Redaction.On
        )
        {
            _AddHeadlessRedaction(builder.Services);
        }

        if (pipeline is LogPipeline.MelSerilog or LogPipeline.NativeSerilog)
        {
            logDirectory = Path.Combine(Path.GetTempPath(), "headless-leak-" + Guid.NewGuid().ToString("N"));

            if (redaction == Redaction.On)
            {
                _AddHeadlessRedaction(builder.Services);
                builder.AddHeadlessSerilog(
                    new SerilogOptions
                    {
                        ConsoleFormat = SerilogConsoleFormat.Json,
                        LogDirectory = logDirectory,
                        Buffered = false,
                    },
                    (_, configuration) => _AddConsoleCaptures(configuration, console, consoleText)
                );
            }
            else
            {
                // Serilog as its own packages ship it, with the same sink shapes the Headless profile writes.
                var directory = logDirectory;
                builder.Services.AddSerilog(
                    (_, configuration) =>
                    {
                        configuration.MinimumLevel.Information().Enrich.FromLogContext();
                        _AddConsoleCaptures(configuration, console, consoleText);
                        configuration.WriteTo.File(
                            new MessageTemplateTextFormatter(
                                SerilogFactory.OutputTemplate,
                                CultureInfo.InvariantCulture
                            ),
                            Path.Combine(directory, "warning-.log"),
                            rollingInterval: RollingInterval.Day
                        );
                    },
                    preserveStaticLogger: true,
                    writeToProviders: true
                );
            }
        }

        return new LogLeakHost(builder.Build(), console, consoleText, otlp, logDirectory);
    }

    /// <summary>Disposes the host, which flushes every sink, and returns what each received.</summary>
    public Exported Collect()
    {
        Dispose();

        var sinks = new Dictionary<Sink, byte[]> { [Sink.OtlpLogs] = _otlp.Bytes("logs") };

        sinks[Sink.Console] = Encoding.UTF8.GetBytes(_console.ToString());

        if (_logDirectory is not null)
        {
            sinks[Sink.ConsoleText] = Encoding.UTF8.GetBytes(_consoleText.ToString());
            sinks[Sink.File] = Directory.Exists(_logDirectory)
                ? [.. Directory.EnumerateFiles(_logDirectory, "*.log").SelectMany(File.ReadAllBytes)]
                : [];
        }

        return new Exported(sinks);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Dispose();
        _otlp.Dispose();
    }

    public void DeleteFiles()
    {
        if (_logDirectory is not null && Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private static void _AddHeadlessRedaction(IServiceCollection services)
    {
        services.AddHeadlessRedaction(setup =>
            setup.MaskText(Pii.NationalIdPattern).ClassifyProperty("Email", HeadlessDataClassifications.Private)
        );
    }

    private static void _AddConsoleCaptures(LoggerConfiguration configuration, TextWriter json, TextWriter text)
    {
        // The formatters the Headless console sink uses in production (compact JSON) and in Development (the template).
        configuration.WriteTo.Sink(new TextWriterSink(new CompactJsonFormatter(), json));
        configuration.WriteTo.Sink(
            new TextWriterSink(
                new MessageTemplateTextFormatter(SerilogFactory.OutputTemplate, CultureInfo.InvariantCulture),
                text
            )
        );
    }

    private static void _AddJsonConsoleCapture(HostApplicationBuilder builder, TextWriter writer)
    {
        builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
        _CaptureJsonConsole(builder, writer);
    }

    private static void _CaptureJsonConsole(HostApplicationBuilder builder, TextWriter writer)
    {
        // Keep the formatter, drop the provider that would print to the process-wide stdout.
        var console = builder.Services.Where(d => d.ImplementationType == typeof(ConsoleLoggerProvider)).ToList();

        foreach (var descriptor in console)
        {
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddSingleton<ILoggerProvider>(provider => new FormatterCaptureProvider(
            provider
                .GetServices<ConsoleFormatter>()
                .Single(f => string.Equals(f.Name, ConsoleFormatterNames.Json, StringComparison.Ordinal)),
            writer
        ));
    }
}

/// <summary>Writes each Serilog event through a formatter into a <see cref="TextWriter"/>, as the console sink would.</summary>
internal sealed class TextWriterSink(ITextFormatter formatter, TextWriter writer) : ILogEventSink
{
    private readonly Lock _lock = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_lock)
        {
            formatter.Format(logEvent, writer);
        }
    }
}
