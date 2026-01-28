// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.OpenTelemetry;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

public static class MessagingOpenTelemetryTracingSetup
{
    /// <summary>
    /// Enables the message eventing data collection for messaging.
    /// </summary>
    /// <param name="builder"><see cref="TracerProviderBuilder" /> being configured.</param>
    /// <param name="enableMetrics">Whether to enable OpenTelemetry metrics collection (default: false).</param>
    /// <returns>The instance of <see cref="TracerProviderBuilder" /> to chain the calls.</returns>
    public static TracerProviderBuilder AddMessagingInstrumentation(
        this TracerProviderBuilder builder,
        bool enableMetrics = false
    )
    {
        Argument.IsNotNull(builder);

        builder.AddSource(DiagnosticListener.SourceName);

        return builder.AddInstrumentation(() =>
        {
            var metrics = enableMetrics ? new MessagingMetrics() : null;
            return new MessagingInstrumentation(new DiagnosticListener(metrics), metrics);
        });
    }
}
