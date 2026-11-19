// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;

namespace OpenTelemetry.Metrics;

/// <summary>
/// OpenTelemetry metrics registration for the host's startup. Lives in the <c>OpenTelemetry.Metrics</c> namespace so
/// it surfaces next to the provider builder without an extra import; only <c>OpenTelemetry.Api</c> surface is used (no
/// SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessHostingMeteringExtensions
{
    /// <summary>
    /// Enables the host's startup metrics by subscribing to the <see cref="HostingDiagnostics.MeterName" /> meter.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder" /> being configured.</param>
    /// <returns>The same <paramref name="builder" /> for chaining.</returns>
    public static MeterProviderBuilder AddHostingInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddMeter(HostingDiagnostics.MeterName);
    }
}
