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
    /// <summary>The default Serilog console output template shared with <see cref="SerilogFactory"/>.</summary>
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

    /// <summary>
    /// Applies Headless API-specific enrichers on top of the base reloadable logger configuration:
    /// client IP address, and sanitized values for the <c>User-Agent</c>, <c>X-Client-Version</c>, and
    /// <c>X-Api-Version</c> request headers (each truncated to <see cref="SerilogOptions.MaxHeaderLength"/>
    /// characters and stripped of control characters and ANSI escape sequences).
    /// </summary>
    /// <param name="loggerConfiguration">The Serilog logger configuration to extend.</param>
    /// <param name="services">
    /// Optional service provider used to resolve <c>IHttpContextAccessor</c>. When <see langword="null"/>,
    /// a new <c>HttpContextAccessor</c> instance is created.
    /// </param>
    /// <param name="configuration">The application configuration (used by the base reloadable setup).</param>
    /// <param name="environment">The host environment (used to set the environment enricher).</param>
    /// <param name="options">Optional tuning options; defaults are applied when <see langword="null"/>.</param>
    /// <returns><paramref name="loggerConfiguration"/> for chaining.</returns>
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
