// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using OpenTelemetry.Metrics;

namespace Headless.Messaging.OpenTelemetry;

[PublicAPI]
public static class SetupMetrics
{
    /// <summary>
    /// Enables OpenTelemetry metrics collection for messaging operations.
    /// </summary>
    /// <param name="builder"><see cref="MeterProviderBuilder" /> being configured.</param>
    /// <returns>The instance of <see cref="MeterProviderBuilder" /> to chain the calls.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public static MeterProviderBuilder AddMessagingInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.AddMeter(MessagingMetrics.MeterName);

        return builder;
    }
}
