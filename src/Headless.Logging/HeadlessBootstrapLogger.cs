// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Logging;

/// <summary>A console logger for the code that runs before the host exists.</summary>
/// <example>
/// <code>
/// using var bootstrap = HeadlessBootstrapLogger.Create();
///
/// try
/// {
///     var builder = WebApplication.CreateBuilder(args);
///     builder.AddHeadless();
///     await builder.Build().RunAsync();
/// }
/// catch (Exception exception)
/// {
///     bootstrap.CreateLogger("Startup").LogCritical(exception, "The host terminated unexpectedly");
///     throw;
/// }
/// </code>
/// </example>
[PublicAPI]
public static class HeadlessBootstrapLogger
{
    /// <summary>
    /// Creates a logger factory that writes to the console in the Headless format, chosen from the
    /// <c>ASPNETCORE_ENVIRONMENT</c> and then <c>DOTNET_ENVIRONMENT</c> variables, because no configuration exists yet.
    /// </summary>
    /// <remarks>
    /// It reads no configuration and logs at <see cref="LogLevel.Information"/> and above. Dispose it before the process
    /// exits: the console writes on a background queue, and disposing flushes it.
    /// </remarks>
    /// <param name="options">The console format; the environment decides when omitted.</param>
    /// <returns>A logger factory the caller owns and disposes.</returns>
    public static ILoggerFactory Create(HeadlessLoggingOptions? options = null)
    {
        options ??= new HeadlessLoggingOptions();
        var format = options.ResolveConsoleFormat(_ReadEnvironmentName());

        return LoggerFactory.Create(logging =>
            logging.SetMinimumLevel(LogLevel.Information).AddHeadlessConsole(format)
        );
    }

    // The web host lets ASPNETCORE_ENVIRONMENT override DOTNET_ENVIRONMENT, so it is checked first.
    private static string? _ReadEnvironmentName()
    {
        var name = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        return string.IsNullOrWhiteSpace(name) ? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") : name;
    }
}
