using System.Net;
using Framework.Kernel.BuildingBlocks.Helpers.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NetTools;
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

    public static LoggerConfiguration CreateBaseReloadableLoggerConfiguration(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var loggerConfiguration = new LoggerConfiguration();

        return loggerConfiguration.ConfigureBaseReloadableLoggerConfiguration(services, configuration, environment);
    }

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
            .Destructure.ByTransforming<IPAddressRange?>(ip => ip?.ToString() ?? "")
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
