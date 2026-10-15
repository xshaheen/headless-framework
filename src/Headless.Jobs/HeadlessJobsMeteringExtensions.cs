// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs;

namespace OpenTelemetry.Metrics;

/// <summary>
/// OpenTelemetry metrics registration for the Jobs subsystem. Lives in the <c>OpenTelemetry.Metrics</c> namespace so it
/// surfaces next to the provider builder without an extra <c>using Headless.Jobs</c> import; only
/// <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessJobsMeteringExtensions
{
    /// <summary>
    /// Enables Jobs metrics by subscribing to the <see cref="JobsDiagnostics.SourceName" /> meter. Subscribing is the
    /// only opt-in: the instruments record nothing until a listener attaches.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder" /> being configured.</param>
    /// <returns>The same <paramref name="builder" /> for chaining.</returns>
    public static MeterProviderBuilder AddJobsInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddMeter(JobsDiagnostics.SourceName);
    }
}
