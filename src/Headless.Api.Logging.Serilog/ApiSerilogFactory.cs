// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Logging.Enrichers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;

namespace Headless.Logging;

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
    public static Logger CreateApiLogger(WebApplication app, SerilogOptions? options = null)
    {
        var configuration = CreateApiLoggerConfiguration(app.Services, app.Configuration, app.Environment, options);

        return configuration.CreateLogger();
    }

    /// <inheritdoc cref="ConfigureApiLoggerConfiguration"/>
    public static Logger CreateApiLogger(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        SerilogOptions? options = null
    )
    {
        return CreateApiLoggerConfiguration(services, configuration, environment, options).CreateLogger();
    }

    /// <inheritdoc cref="ConfigureApiLoggerConfiguration"/>
    public static LoggerConfiguration CreateApiLoggerConfiguration(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        SerilogOptions? options = null
    )
    {
        var loggerConfiguration = new LoggerConfiguration();

        return loggerConfiguration.ConfigureApiLoggerConfiguration(services, configuration, environment, options);
    }

    public static LoggerConfiguration ConfigureApiLoggerConfiguration(
        this LoggerConfiguration loggerConfiguration,
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        SerilogOptions? options = null
    )
    {
        options ??= new SerilogOptions();

        loggerConfiguration.ConfigureReloadableLoggerConfiguration(services, configuration, environment, options);

        loggerConfiguration
            .Enrich.WithClientIp()
            .Enrich.WithSanitizedRequestHeader(HttpHeaderNames.UserAgent, maxLength: options.MaxHeaderLength)
            .Enrich.WithSanitizedRequestHeader(HttpHeaderNames.ClientVersion, maxLength: options.MaxHeaderLength)
            .Enrich.WithSanitizedRequestHeader(HttpHeaderNames.ApiVersion, maxLength: options.MaxHeaderLength);

        return loggerConfiguration;
    }

    #endregion
}
