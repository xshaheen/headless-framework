// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

[PublicAPI]
public static class SetupMessagingOpenTelemetry
{
    /// <summary>
    /// Enables messaging OpenTelemetry tracing instrumentation.
    /// </summary>
    /// <param name="builder"><see cref="TracerProviderBuilder" /> being configured.</param>
    /// <param name="configure">Optional delegate to configure <see cref="MessagingInstrumentationOptions"/>.</param>
    /// <returns>The instance of <see cref="TracerProviderBuilder" /> to chain the calls.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static TracerProviderBuilder AddMessagingInstrumentation(
        this TracerProviderBuilder builder,
        Action<MessagingInstrumentationOptions>? configure = null
    )
    {
        Argument.IsNotNull(builder);

        var options = new MessagingInstrumentationOptions();
        configure?.Invoke(options);

        var enrichers = options.BuildEnrichers();

        builder.AddSource(DiagnosticListener.SourceName);

        return builder.AddInstrumentation(sp =>
        {
            var logger = sp.GetService<ILogger<DiagnosticListener>>();
            var metrics = options.EnableMetrics ? new MessagingMetrics() : null;
            return new MessagingInstrumentation(new DiagnosticListener(enrichers, logger, metrics), metrics);
        });
    }
}
