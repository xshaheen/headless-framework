// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using Headless.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Settings.Configuration;
using Serilog.Sinks.SystemConsole.Themes;

namespace Headless.Logging;

/// <summary>
/// Factory methods and extension methods for building Serilog <see cref="LoggerConfiguration"/>
/// instances with Headless-standard enrichers, sinks, and structured-logging defaults.
/// </summary>
/// <remarks>
/// Two configuration profiles are provided:
/// <list type="bullet">
///   <item>
///     <term>Bootstrap</term>
///     <description>
///     A lightweight configuration for the very early startup phase, before the DI container or
///     <see cref="IConfiguration"/> are available. Writes to the console and an async file sink
///     (Fatal/Error/Warning only). Use <see cref="ConfigureBootstrapLoggerConfiguration"/> (or
///     its factory wrapper <see cref="CreateBootstrapLoggerConfiguration"/>) with
///     <c>Log.Logger = Log.Logger.CreateBootstrapLogger()</c>.
///     </description>
///   </item>
///   <item>
///     <term>Reloadable</term>
///     <description>
///     The full production configuration, applied after the host is built. Reads from the
///     <c>Serilog</c> configuration section, adds richer enrichers (span, application version,
///     commit hash), and optionally writes to per-severity rolling files. Use
///     <see cref="ConfigureReloadableLoggerConfiguration"/> (or its factory wrapper
///     <see cref="CreateReloadableLoggerConfiguration"/>) with
///     <c>UseSerilog((ctx, sp, cfg) => cfg.ConfigureReloadableLoggerConfiguration(...))</c>.
///     </description>
///   </item>
/// </list>
/// </remarks>
[PublicAPI]
public static class SerilogFactory
{
    /// <summary>
    /// The default Serilog output template used by all console and file sinks in this factory.
    /// Includes timestamp, level (3-char uppercase), request path, source context, message, and
    /// exception on a new line.
    /// </summary>
    public const string OutputTemplate =
        "[{Timestamp:HH:mm:ss.fff zzz} {Level:u3}] {RequestPath} {SourceContext} {Message:lj}{NewLine}{Exception}";

    private static readonly Func<IPAddress?, string> _IpAddressTransform = ip => ip?.ToString() ?? string.Empty;

    /// <summary>
    /// Creates a new <see cref="LoggerConfiguration"/> configured for the bootstrap (pre-DI) phase.
    /// </summary>
    /// <param name="options">
    /// Optional Serilog options controlling file sink behaviour. Defaults are applied when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// A <see cref="LoggerConfiguration"/> ready to be passed to
    /// <c>Log.Logger = configuration.CreateBootstrapLogger()</c>.
    /// </returns>
    public static LoggerConfiguration CreateBootstrapLoggerConfiguration(SerilogOptions? options = null)
    {
        var loggerConfiguration = new LoggerConfiguration();

        loggerConfiguration.ConfigureBootstrapLoggerConfiguration(options);

        return loggerConfiguration;
    }

    /// <summary>
    /// Applies the Headless bootstrap logging profile to <paramref name="loggerConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// The bootstrap profile is intentionally minimal, suitable for the pre-DI startup phase:
    /// <list type="bullet">
    ///   <item>Enrichers: environment name, thread ID, process ID, process name, machine name.</item>
    ///   <item>Console sink (invariant culture, no theme).</item>
    ///   <item>Debug sink (DEBUG builds only).</item>
    ///   <item>Async file sink at <c>{LogDirectory}/bootstrap-.log</c> limited to Fatal, Error, and Warning levels.</item>
    /// </list>
    /// </remarks>
    /// <param name="loggerConfiguration">The <see cref="LoggerConfiguration"/> to configure.</param>
    /// <param name="options">
    /// Optional Serilog options controlling file sink behaviour. Defaults are applied when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>The same <paramref name="loggerConfiguration"/> instance so calls can be chained.</returns>
    public static LoggerConfiguration ConfigureBootstrapLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        SerilogOptions? options = null
    )
    {
        options ??= new SerilogOptions();

        loggerConfiguration
            .Destructure.ByTransforming(_IpAddressTransform)
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithProcessName()
            .Enrich.WithMachineName()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);

        loggerConfiguration.WriteTo.Async(sink =>
        {
            sink.Logger(logger =>
                logger
                    .Filter.ByIncludingOnly(x =>
                        x.Level is LogEventLevel.Fatal or LogEventLevel.Error or LogEventLevel.Warning
                    )
                    .WriteTo._File(
                        formatter: new MessageTemplateTextFormatter(OutputTemplate),
                        path: $"{options.LogDirectory}/bootstrap-.log",
                        options: options
                    )
            );
        });

        _WriteToDebug(loggerConfiguration);

        return loggerConfiguration;
    }

    /// <summary>
    /// Creates a new <see cref="LoggerConfiguration"/> configured for the full production (reloadable) phase.
    /// </summary>
    /// <param name="services">
    /// The application <see cref="IServiceProvider"/>, used to read Serilog sink registrations from
    /// DI. Pass <see langword="null"/> when DI is not yet available (design-time tooling).
    /// </param>
    /// <param name="configuration">
    /// The host configuration from which the <c>Serilog</c> section is read.
    /// </param>
    /// <param name="environment">The host environment, used to select console theme and detect development mode.</param>
    /// <returns>
    /// A <see cref="LoggerConfiguration"/> ready to be passed to
    /// <c>UseSerilog((ctx, sp, cfg) => cfg.ConfigureReloadableLoggerConfiguration(...))</c>.
    /// </returns>
    public static LoggerConfiguration CreateReloadableLoggerConfiguration(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var loggerConfiguration = new LoggerConfiguration();

        return loggerConfiguration.ConfigureReloadableLoggerConfiguration(services, configuration, environment);
    }

    /// <summary>
    /// Applies the Headless production (reloadable) logging profile to <paramref name="loggerConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// The reloadable profile is richer than the bootstrap profile and reads from the host configuration:
    /// <list type="bullet">
    ///   <item>Reads the <c>Serilog</c> configuration section, enabling log-level overrides without a restart.</item>
    ///   <item>When <paramref name="services"/> is not null, also reads DI-registered sink/enricher contributions.</item>
    ///   <item>Enrichers: log context, distributed tracing span, environment name, thread/process/machine IDs,
    ///   application name, version, and commit hash (from entry-assembly metadata).</item>
    ///   <item>Console sink with ANSI theme in Development, no theme otherwise.</item>
    ///   <item>Debug sink (DEBUG builds only).</item>
    ///   <item>Per-severity rolling file sinks (Fatal, Error, Warning) when <see cref="SerilogOptions.WriteToFiles"/> is <see langword="true"/>.</item>
    /// </list>
    /// </remarks>
    /// <param name="loggerConfiguration">The <see cref="LoggerConfiguration"/> to configure.</param>
    /// <param name="services">
    /// The application <see cref="IServiceProvider"/>, used to read Serilog sink registrations from
    /// DI. Pass <see langword="null"/> when DI is not yet available.
    /// </param>
    /// <param name="configuration">
    /// The host configuration from which the <c>Serilog</c> section is read.
    /// </param>
    /// <param name="environment">The host environment, used to select console theme and detect development mode.</param>
    /// <param name="options">
    /// Optional Serilog options controlling file sink behaviour. Defaults are applied when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>The same <paramref name="loggerConfiguration"/> instance so calls can be chained.</returns>
    public static LoggerConfiguration ConfigureReloadableLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        SerilogOptions? options = null
    )
    {
        options ??= new SerilogOptions();

        loggerConfiguration
            .ReadFrom.Configuration(
                configuration,
                new ConfigurationReaderOptions
                {
                    SectionName = "Serilog",
                    FormatProvider = CultureInfo.InvariantCulture,
                }
            )
            .Destructure.ByTransforming(_IpAddressTransform)
            .Enrich.FromLogContext()
            .Enrich.WithSpan()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithProcessName()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", environment.ApplicationName)
            .Enrich.WithProperty("Version", AssemblyInformation.Entry?.Version)
            .Enrich.WithProperty("CommitHash", AssemblyInformation.Entry?.CommitNumber);

        if (services is not null)
        {
            loggerConfiguration.ReadFrom.Services(services);
        }

        var isDev = environment.IsDevelopment();

        _WriteToDebug(loggerConfiguration);
        loggerConfiguration._WriteToConsole(isDev ? AnsiConsoleTheme.Code : ConsoleTheme.None);

        if (options.WriteToFiles)
        {
            loggerConfiguration._WriteToLogFiles(new MessageTemplateTextFormatter(OutputTemplate), options);
        }

        return loggerConfiguration;
    }

    private static void _WriteToConsole(this LoggerConfiguration loggerConfiguration, ConsoleTheme theme)
    {
        loggerConfiguration.WriteTo.Console(
            outputTemplate: OutputTemplate,
            formatProvider: CultureInfo.InvariantCulture,
            theme: theme
        );
    }

    [Conditional("DEBUG")]
    private static void _WriteToDebug(LoggerConfiguration loggerConfiguration)
    {
        loggerConfiguration.WriteTo.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture);
    }

    private static void _WriteToLogFiles(
        this LoggerConfiguration loggerConfiguration,
        ITextFormatter textFormatter,
        SerilogOptions options
    )
    {
        loggerConfiguration.WriteTo.Async(sink =>
            sink.Logger(logger =>
                    logger
                        .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Fatal)
                        .WriteTo._File(
                            formatter: textFormatter,
                            path: $"{options.LogDirectory}/fatal-.log",
                            options: options
                        )
                )
                .WriteTo.Logger(logger =>
                    logger
                        .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Error)
                        .WriteTo._File(
                            formatter: textFormatter,
                            path: $"{options.LogDirectory}/error-.log",
                            options: options
                        )
                )
                .WriteTo.Logger(logger =>
                    logger
                        .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Warning)
                        .WriteTo._File(
                            formatter: textFormatter,
                            path: $"{options.LogDirectory}/warning-.log",
                            options: options
                        )
                )
        );
    }

    private static LoggerConfiguration _File(
        this LoggerSinkConfiguration config,
        ITextFormatter formatter,
        string path,
        SerilogOptions options
    )
    {
        return config.File(
            formatter: formatter,
            path: path,
            buffered: options.Buffered,
            flushToDiskInterval: options.FlushToDiskInterval,
            rollingInterval: options.RollingInterval,
            retainedFileCountLimit: options.RetainedFileCountLimit
        );
    }
}
