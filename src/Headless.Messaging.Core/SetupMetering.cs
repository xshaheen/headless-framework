// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace OpenTelemetry.Metrics;

/// <summary>
/// OpenTelemetry metrics registration for the messaging subsystem. Lives in the <c>OpenTelemetry.Metrics</c>
/// namespace so it surfaces next to the provider builder without an extra <c>using Headless.Messaging</c> import;
/// only <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessMessagingMeteringExtensions
{
    /// <summary>
    /// Enables messaging metrics by subscribing to the <see cref="MessagingDiagnostics.SourceName"/> meter.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> being configured.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MeterProviderBuilder AddMessagingInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddMeter(MessagingDiagnostics.SourceName);
    }
}
