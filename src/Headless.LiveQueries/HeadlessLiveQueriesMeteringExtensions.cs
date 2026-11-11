// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.LiveQueries;

namespace OpenTelemetry.Metrics;

/// <summary>
/// OpenTelemetry metrics registration for the live-queries subsystem. Lives in the <c>OpenTelemetry.Metrics</c>
/// namespace so it surfaces next to the provider builder without an extra <c>using Headless.LiveQueries</c> import;
/// only <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessLiveQueriesMeteringExtensions
{
    /// <summary>
    /// Enables live-queries metrics by subscribing to the <see cref="LiveQueriesDiagnostics.SourceName" /> meter,
    /// which carries the instruments of the hub, of live endpoints, and of tracked data sources.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder" /> being configured.</param>
    /// <returns>The same <paramref name="builder" /> for chaining.</returns>
    public static MeterProviderBuilder AddLiveQueriesInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddMeter(LiveQueriesDiagnostics.SourceName);
    }
}
