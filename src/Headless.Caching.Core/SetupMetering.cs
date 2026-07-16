// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace OpenTelemetry.Metrics;

/// <summary>
/// OpenTelemetry metrics registration for the caching subsystem. Lives in the <c>OpenTelemetry.Metrics</c>
/// namespace so it surfaces next to the provider builder without an extra <c>using Headless.Caching</c> import;
/// only <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessCachingMeteringExtensions
{
    /// <summary>
    /// Enables caching metrics by subscribing to the <see cref="CachingDiagnostics.SourceName"/> meter.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> being configured.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static MeterProviderBuilder AddCachingInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddMeter(CachingDiagnostics.SourceName);
    }
}
