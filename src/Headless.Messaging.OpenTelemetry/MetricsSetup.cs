// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.OpenTelemetry;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace OpenTelemetry.Metrics;

public static class MessagingOpenTelemetryMetricsSetup
{
    /// <summary>
    /// Enables OpenTelemetry metrics collection for messaging operations.
    /// </summary>
    /// <param name="builder"><see cref="MeterProviderBuilder" /> being configured.</param>
    /// <returns>The instance of <see cref="MeterProviderBuilder" /> to chain the calls.</returns>
    public static MeterProviderBuilder AddMessagingInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.AddMeter(MessagingMetrics.MeterName);

        return builder;
    }
}
