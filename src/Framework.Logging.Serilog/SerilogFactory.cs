// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using Framework.Reflection;
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

namespace Framework.Logging;

[PublicAPI]
public static class SerilogFactory
{
    public const string OutputTemplate =
        "[{Timestamp:HH:mm:ss.fff zzz} {Level:u3}] {RequestPath} {SourceContext} {Message:lj}{NewLine}{Exception}";

    private static readonly Func<IPAddress?, string> _IpAddressTransform = ip => ip?.ToString() ?? string.Empty;

    /// <inheritdoc cref="CreateBootstrapLoggerConfiguration"/>
    public static LoggerConfiguration CreateBootstrapLoggerConfiguration()
    {
        var loggerConfiguration = new LoggerConfiguration();

        loggerConfiguration.ConfigureBootstrapLoggerConfiguration();

        return loggerConfiguration;
    }

    /// <summary>
    /// Creates a bootstrap logger configuration with various enrichers and sinks.
    /// </summary>
    /// <remarks>
    /// This method configures a <see cref="LoggerConfiguration"/> instance with the following:
    /// <list type="bullet">
    ///   <item>Enrichers for environment name, username, thread ID, process ID, process name, and machine name.</item>
    ///   <item>Console sink for logging to the console.</item>
    ///   <item>Debug sink for logging to the debug output in debug mode.</item>
    ///  <item>Asynchronous file sink for logging fatal, error, and warning levels to a file with the path "Logs/bootstrap-.log".</item>
    /// </list>
    /// </remarks>
    /// <param name="logDirectory">Directory path for log files. Defaults to "Logs".</param>
    /// <returns>A <see cref="LoggerConfiguration"/> instance configured for bootstrap logging.</returns>
    public static LoggerConfiguration ConfigureBootstrapLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        string logDirectory = "Logs"
    )
    {
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
                        path: $"{logDirectory}/bootstrap-.log"
                    )
            );
        });

        _WriteToDebug(loggerConfiguration);

        return loggerConfiguration;
    }

    /// <inheritdoc cref="ConfigureReloadableLoggerConfiguration"/>
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
    /// Configures a <see cref="LoggerConfiguration"/> instance with various enrichers and sinks for reloadable logging.
    /// </summary>
    /// <remarks>
    /// This method configures the logger with the following:
    /// <list type="bullet">
    ///   <item>Reads configuration settings from the provided <see cref="IConfiguration"/> instance.</item>
    ///   <item>If a <see cref="IServiceProvider"/> is provided, reads additional configuration from the services.</item>
    ///   <item>Adds various enrichers to include context information such as log context, span, environment name, username, thread ID, process ID, process name, machine name, application name, version, and commit hash.</item>
    ///   <item>Configures console sink.</item>
    ///   <item>In debug mode, adds a debug sink for logging to the debug output.</item>
    /// </list>
    /// </remarks>
    /// <param name="logDirectory">Directory path for log files. Defaults to "Logs".</param>
    public static LoggerConfiguration ConfigureReloadableLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool writeToFiles = true,
        string logDirectory = "Logs"
    )
    {
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
            .Enrich.WithProperty("Version", AssemblyInformation.Entry.Version)
            .Enrich.WithProperty("CommitHash", AssemblyInformation.Entry.CommitNumber);

        if (services is not null)
        {
            loggerConfiguration.ReadFrom.Services(services);
        }

        var isDev = environment.IsDevelopment();

        _WriteToDebug(loggerConfiguration);
        loggerConfiguration._WriteToConsole(isDev ? AnsiConsoleTheme.Code : ConsoleTheme.None);

        if (writeToFiles)
        {
            loggerConfiguration._WriteToLogFiles(new MessageTemplateTextFormatter(OutputTemplate), logDirectory);
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
        string logDirectory
    )
    {
        loggerConfiguration.WriteTo.Async(sink =>
            sink.Logger(logger =>
                    logger
                        .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Fatal)
                        .WriteTo._File(formatter: textFormatter, path: $"{logDirectory}/fatal-.log")
                )
                .WriteTo.Logger(logger =>
                    logger
                        .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Error)
                        .WriteTo._File(formatter: textFormatter, path: $"{logDirectory}/error-.log")
                )
                .WriteTo.Logger(logger =>
                    logger
                        .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Warning)
                        .WriteTo._File(formatter: textFormatter, path: $"{logDirectory}/warning-.log")
                )
        );
    }

    private static LoggerConfiguration _File(this LoggerSinkConfiguration config, ITextFormatter formatter, string path)
    {
        return config.File(
            formatter: formatter,
            path: path,
            buffered: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 5
        );
    }
}
