// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;

namespace Framework.Logging;

[PublicAPI]
public static class ApiSerilogFactory
{
    public const string OutputTemplate = SerilogFactory.OutputTemplate;

    #region Bootstrap

    /// <inheritdoc cref="SerilogFactory.CreateBootstrapLoggerConfiguration"/>
    public static Logger CreateApiBootstrapLogger()
    {
        return CreateApiBootstrapLoggerConfiguration().CreateLogger();
    }

    /// <inheritdoc cref="SerilogFactory.CreateBootstrapLoggerConfiguration"/>
    public static LoggerConfiguration CreateApiBootstrapLoggerConfiguration()
    {
        return SerilogFactory.CreateBootstrapLoggerConfiguration();
    }

    #endregion

    #region Reloadable

    /// <inheritdoc cref="ConfigureApiLoggerConfiguration"/>
    public static Logger CreateApiLogger(WebApplication app, bool writeToFiles = true)
    {
        var configuration = CreateApiLoggerConfiguration(
            app.Services,
            app.Configuration,
            app.Environment,
            writeToFiles
        );

        return configuration.CreateLogger();
    }

    /// <inheritdoc cref="ConfigureApiLoggerConfiguration"/>
    public static Logger CreateApiLogger(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool writeToFiles = true
    )
    {
        return CreateApiLoggerConfiguration(services, configuration, environment, writeToFiles).CreateLogger();
    }

    /// <inheritdoc cref="ConfigureApiLoggerConfiguration"/>
    public static LoggerConfiguration CreateApiLoggerConfiguration(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool writeToFiles = true
    )
    {
        var loggerConfiguration = new LoggerConfiguration();

        return loggerConfiguration.ConfigureApiLoggerConfiguration(services, configuration, environment, writeToFiles);
    }

    public static LoggerConfiguration ConfigureApiLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool writeToFiles = true
    )
    {
        loggerConfiguration.ConfigureReloadableLoggerConfiguration(services, configuration, environment, writeToFiles);

        loggerConfiguration
            .Enrich.WithClientIp()
            .Enrich.WithCorrelationId()
            .Enrich.WithRequestHeader(HttpHeaderNames.UserAgent)
            .Enrich.WithRequestHeader(HttpHeaderNames.ClientVersion)
            .Enrich.WithRequestHeader(HttpHeaderNames.ApiVersion);

        return loggerConfiguration;
    }

    #endregion
}
