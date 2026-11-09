// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Headless.Logging;

/// <summary>Registers Serilog as the host's logger with the Headless profile and log redaction.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupLoggingSerilog
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Makes Serilog the host's logger, configured by
        /// <see cref="SerilogFactory.ConfigureReloadableLoggerConfiguration"/>, forwarding every event to the other
        /// logger providers (OpenTelemetry), and redacted by <c>AddHeadlessRedaction</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use it instead of <c>UseSerilog</c>. It wraps Serilog's logger factory itself, so it works in any order with
        /// <c>AddHeadlessRedaction</c>, <c>AddHeadless</c>, and the libraries that call them: no call has to come after
        /// it. A bootstrap logger assigned to <c>Log.Logger</c> before the host is built is reloaded, as with
        /// <c>UseSerilog</c>.
        /// </para>
        /// <para>
        /// Events are forwarded to the other registered providers, so call <c>builder.Logging.ClearProviders()</c>
        /// before adding OpenTelemetry (<c>AddHeadless</c>) when the default Console, Debug, and EventSource providers
        /// should not print each event a second time.
        /// </para>
        /// </remarks>
        /// <param name="options">The console format and file output; the defaults when omitted.</param>
        /// <param name="configure">Runs after the Headless profile, to add sinks or enrichers.</param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        public IHostApplicationBuilder AddHeadlessSerilog(
            SerilogOptions? options = null,
            Action<IServiceProvider, LoggerConfiguration>? configure = null
        )
        {
            Argument.IsNotNull(builder);

            var configuration = builder.Configuration;
            var environment = builder.Environment;

            builder.Services.AddSerilog(
                (services, loggerConfiguration) =>
                {
                    loggerConfiguration.ConfigureReloadableLoggerConfiguration(
                        services,
                        configuration,
                        environment,
                        options
                    );
                    configure?.Invoke(services, loggerConfiguration);
                },
                writeToProviders: true
            );

            // After Serilog's factory, so the wrapper is the one the container resolves.
            builder.Services.AddHeadlessRedaction();

            return builder;
        }
    }
}
