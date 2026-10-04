// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.Caching;

/// <summary>
/// Provides shared <see cref="ActivitySource"/> and <see cref="Meter"/> instances for the caching subsystem.
/// </summary>
[PublicAPI]
public static class CachingDiagnostics
{
    /// <summary>Represents the activity source and meter name used by caching telemetry.</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "Caching";

    /// <summary>Represents the <c>headless.cache.name</c> value used for the unkeyed default cache instance.</summary>
    public const string DefaultCacheName = "default";

    /// <summary>Shared <see cref="ActivitySource"/> for caching traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Caching");

    /// <summary>Shared <see cref="Meter"/> for caching metrics.</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("Caching");

    /// <summary>
    /// Gets whether any span or metric listener is attached to the caching scope. Emit sites gate span creation
    /// and <see cref="System.Diagnostics.TagList"/> building on this so an unobserved cache pays no instrumentation
    /// cost on the hot path.
    /// </summary>
    internal static bool IsEnabled => ActivitySource.HasListeners() || CachingMetrics.AnyEnabled;

    /// <summary>Starts a caching <see cref="Activity"/> if a listener is attached; otherwise returns null.</summary>
    /// <param name="name">The activity operation name (for example <c>cache.get_or_add</c>).</param>
    /// <param name="kind">The activity kind; defaults to <see cref="ActivityKind.Internal"/>.</param>
    /// <returns>The started activity, or <see langword="null"/> when no listener is subscribed.</returns>
    internal static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}
