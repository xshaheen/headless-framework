// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Framework.BuildingBlocks.Helpers.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Sinks.SystemConsole.Themes;

namespace Framework.Logging.Serilog;

[PublicAPI]
public static class SerilogFactory
{
    public const string OutputTemplate =
        "[{Timestamp:HH:mm:ss.fff zzz} {Level:u3}] ({RequestPath}) <src:{SourceContext}>{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}";

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
    /// <returns>A <see cref="LoggerConfiguration"/> instance configured for bootstrap logging.</returns>
    public static LoggerConfiguration CreateBootstrapLoggerConfiguration()
    {
        var loggerConfiguration = new LoggerConfiguration()
            .Destructure.ByTransforming<IPAddress?>(ip => ip?.ToString() ?? "")
            .Enrich.WithEnvironmentName()
            .Enrich.WithEnvironmentUserName()
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
                    .WriteTo.File(
                        formatter: new MessageTemplateTextFormatter(OutputTemplate),
                        path: "Logs/bootstrap-.log",
                        shared: true,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 5
                    )
            );
        });

#if DEBUG
        loggerConfiguration.WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture);
#endif

        return loggerConfiguration;
    }

    /// <inheritdoc cref="ConfigureBaseReloadableLoggerConfiguration"/>
    public static LoggerConfiguration CreateBaseReloadableLoggerConfiguration(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var loggerConfiguration = new LoggerConfiguration();

        return loggerConfiguration.ConfigureBaseReloadableLoggerConfiguration(services, configuration, environment);
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
    public static LoggerConfiguration ConfigureBaseReloadableLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        loggerConfiguration
            .ReadFrom.Configuration(configuration)
            .Destructure.ByTransforming<IPAddress?>(ip => ip?.ToString() ?? "")
            .Enrich.FromLogContext()
            .Enrich.WithSpan()
            .Enrich.WithEnvironmentName()
            .Enrich.WithEnvironmentUserName()
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

        var isDevelopment = environment.IsDevelopment();

        loggerConfiguration.WriteTo.Console(
            outputTemplate: OutputTemplate,
            formatProvider: CultureInfo.InvariantCulture,
            theme: isDevelopment ? AnsiConsoleTheme.Code : ConsoleTheme.None
        );

#if DEBUG
        loggerConfiguration.WriteTo.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture);
#endif

        return loggerConfiguration;
    }
}
