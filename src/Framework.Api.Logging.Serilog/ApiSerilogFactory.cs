using Framework.Kernel.BuildingBlocks;
using Framework.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

namespace Framework.Api.Logging.Serilog;

public static class ApiSerilogFactory
{
    public const string OutputTemplate = SerilogFactory.OutputTemplate;

    public static LoggerConfiguration CreateApiBootstrapLoggerConfiguration() =>
        SerilogFactory.CreateBootstrapLoggerConfiguration();

    public static LoggerConfiguration CreateApiBaseReloadableLoggerConfiguration(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var loggerConfiguration = new LoggerConfiguration();

        return loggerConfiguration.ConfigureBaseReloadableLoggerConfiguration(services, configuration, environment);
    }

    public static LoggerConfiguration ConfigureApiBaseReloadableLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        loggerConfiguration.ConfigureBaseReloadableLoggerConfiguration(services, configuration, environment);

        ITextFormatter textFormatter = environment.IsDevelopment()
            ? new MessageTemplateTextFormatter(OutputTemplate)
            : new CompactJsonFormatter();

        loggerConfiguration
            .Enrich.WithThreadId()
            .Enrich.WithClientIp()
            .Enrich.WithCorrelationId()
            .Enrich.WithRequestHeader(HttpHeaderNames.UserAgent)
            .Enrich.WithRequestHeader(HttpHeaderNames.ClientVersion)
            .Enrich.WithRequestHeader(HttpHeaderNames.ApiVersion)
            .WriteTo.Async(sink =>
                sink.Logger(logger =>
                        logger
                            .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Fatal)
                            .WriteTo.File(
                                formatter: textFormatter,
                                path: "Logs/fatal-.log",
                                shared: true,
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 5
                            )
                    )
                    .WriteTo.Logger(logger =>
                        logger
                            .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Error)
                            .WriteTo.File(
                                formatter: textFormatter,
                                path: "Logs/error-.log",
                                shared: true,
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 5
                            )
                    )
                    .WriteTo.Logger(logger =>
                        logger
                            .Filter.ByIncludingOnly(x => x.Level is LogEventLevel.Warning)
                            .WriteTo.File(
                                formatter: textFormatter,
                                path: "Logs/warning-.log",
                                shared: true,
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 5
                            )
                    )
            );

        return loggerConfiguration;
    }
}
