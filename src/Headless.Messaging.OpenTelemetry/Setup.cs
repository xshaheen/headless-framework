// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.OpenTelemetry;
using Headless.Messaging.OpenTelemetry.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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
    public static TracerProviderBuilder AddMessagingInstrumentation(
        this TracerProviderBuilder builder,
        Action<MessagingInstrumentationOptions>? configure = null
    )
    {
        Argument.IsNotNull(builder);

        var options = new MessagingInstrumentationOptions();
        configure?.Invoke(options);

        var enrichers = _BuildEnricherList(options);

        builder.AddSource(DiagnosticListener.SourceName);

        return builder.AddInstrumentation(sp =>
        {
            var logger = sp.GetService<ILogger<DiagnosticListener>>();
            var metrics = options.EnableMetrics ? new MessagingMetrics() : null;
            return new MessagingInstrumentation(new DiagnosticListener(enrichers, logger, metrics), metrics);
        });
    }

    internal static IActivityTagEnricher[] _BuildEnricherList(MessagingInstrumentationOptions options)
    {
        var list = new List<IActivityTagEnricher>();
        if (!options.SuppressTenantIdTag)
        {
            list.Add(new TenantIdTagEnricher());
        }
        if (!options.SuppressRetryCountTag)
        {
            list.Add(new RetryCountTagEnricher());
        }
        list.AddRange(options.Enrichers.Where(e => e is not null));
        return [.. list];
    }
}
