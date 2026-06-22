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

    /// <summary>
    /// Creates a bootstrap <see cref="Logger"/> suitable for capturing log output before the application host
    /// and DI container are fully initialized.
    /// </summary>
    /// <returns>A configured <see cref="Logger"/> instance. The caller is responsible for disposing it.</returns>
    /// <remarks>
    /// Delegates to <see cref="SerilogFactory.ConfigureBootstrapLoggerConfiguration"/> for the base configuration.
    /// The returned logger writes to the console and, for Warning/Error/Fatal events, to a rolling file under
    /// the default log directory.
    /// </remarks>
    public static Logger CreateApiBootstrapLogger()
    {
        return CreateApiBootstrapLoggerConfiguration().CreateLogger();
    }

    /// <summary>
    /// Produces a <see cref="LoggerConfiguration"/> for bootstrap-phase logging before the DI container is available.
    /// </summary>
    /// <returns>A pre-configured <see cref="LoggerConfiguration"/> instance.</returns>
    /// <remarks>
    /// Delegates to <see cref="SerilogFactory.ConfigureBootstrapLoggerConfiguration"/>. Call
    /// <see cref="CreateApiBootstrapLogger"/> to directly obtain the materialized <see cref="Logger"/>.
    /// </remarks>
    public static LoggerConfiguration CreateApiBootstrapLoggerConfiguration()
    {
        return SerilogFactory.CreateBootstrapLoggerConfiguration();
    }

    #endregion

    #region Reloadable

    /// <summary>
    /// Creates a reloadable <see cref="Logger"/> configured with API-specific enrichers, resolving
    /// services, configuration, and environment from the given <see cref="WebApplication"/>.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> whose <c>Services</c>, <c>Configuration</c>, and <c>Environment</c> are used.</param>
    /// <param name="options">Optional tuning options; defaults are applied when <see langword="null"/>.</param>
    /// <returns>A configured <see cref="Logger"/> instance. The caller is responsible for disposing it.</returns>
    /// <remarks>
    /// Adds client IP, <c>User-Agent</c>, <c>X-Client-Version</c>, and <c>X-Api-Version</c> enrichers on top
    /// of the base reloadable logger configuration. See <see cref="ConfigureApiLoggerConfiguration"/> for details.
    /// </remarks>
    public static Logger CreateApiLogger(WebApplication app, SerilogOptions? options = null)
    {
        var configuration = CreateApiLoggerConfiguration(app.Services, app.Configuration, app.Environment, options);

        return configuration.CreateLogger();
    }

    /// <summary>
    /// Creates a reloadable <see cref="Logger"/> configured with API-specific enrichers.
    /// </summary>
    /// <param name="services">
    /// Optional service provider used to resolve <c>IHttpContextAccessor</c>. When <see langword="null"/>,
    /// a new <c>HttpContextAccessor</c> instance is created.
    /// </param>
    /// <param name="configuration">The application configuration (read by <c>Serilog</c> via the <c>Serilog</c> section).</param>
    /// <param name="environment">The host environment (used by the environment enricher and console theme selection).</param>
    /// <param name="options">Optional tuning options; defaults are applied when <see langword="null"/>.</param>
    /// <returns>A configured <see cref="Logger"/> instance. The caller is responsible for disposing it.</returns>
    /// <remarks>
    /// Adds client IP, <c>User-Agent</c>, <c>X-Client-Version</c>, and <c>X-Api-Version</c> enrichers on top
    /// of the base reloadable logger configuration. See <see cref="ConfigureApiLoggerConfiguration"/> for details.
    /// </remarks>
    public static Logger CreateApiLogger(
        IServiceProvider? services,
        IConfiguration configuration,
        IHostEnvironment environment,
        SerilogOptions? options = null
    )
    {
        return CreateApiLoggerConfiguration(services, configuration, environment, options).CreateLogger();
    }

    /// <summary>
    /// Creates a <see cref="LoggerConfiguration"/> configured with API-specific enrichers.
    /// </summary>
    /// <param name="services">
    /// Optional service provider used to resolve <c>IHttpContextAccessor</c>. When <see langword="null"/>,
    /// a new <c>HttpContextAccessor</c> instance is created.
    /// </param>
    /// <param name="configuration">The application configuration (read by <c>Serilog</c> via the <c>Serilog</c> section).</param>
    /// <param name="environment">The host environment (used by the environment enricher and console theme selection).</param>
    /// <param name="options">Optional tuning options; defaults are applied when <see langword="null"/>.</param>
    /// <returns>A pre-configured <see cref="LoggerConfiguration"/> instance.</returns>
    /// <remarks>
    /// Adds client IP, <c>User-Agent</c>, <c>X-Client-Version</c>, and <c>X-Api-Version</c> enrichers on top
    /// of the base reloadable logger configuration. See <see cref="ConfigureApiLoggerConfiguration"/> for details.
    /// </remarks>
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
