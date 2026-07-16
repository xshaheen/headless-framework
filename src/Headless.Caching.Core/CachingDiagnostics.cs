// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Centralises the <see cref="ActivitySource"/> and <see cref="Meter"/> instances used by the caching
/// subsystem. Every provider (in-memory, Redis, hybrid) and the <see cref="FactoryCacheCoordinator"/> share
/// these singletons so traces and metrics land in a single named scope (<c>Headless.Caching</c>). Consumers
/// subscribe via <see cref="SourceName"/> (<c>AddSource</c>/<c>AddMeter</c>) or the typed
/// <c>AddCachingInstrumentation()</c> extensions on the OpenTelemetry provider builders.
/// </summary>
/// <remarks>
/// <para>
/// <b>cache.name threading.</b> The <c>headless.cache.name</c> dimension carries the registered cache-instance
/// name (or <see cref="DefaultCacheName"/> for the unkeyed default). Each provider reads its name from
/// <see cref="CacheOptions.CacheName"/> (set at registration for named instances; <see langword="null"/> for the
/// default) and passes it to the coordinator it constructs. The raw cache <em>key</em> is never a metric
/// dimension and appears on spans only when <see cref="CacheInstrumentationConfig.IncludeKeyInTraces"/> is enabled
/// (default off).
/// </para>
/// <para>
/// <b>Counting model.</b> The <see cref="FactoryCacheCoordinator"/> owns the get-or-add outcome, factory,
/// fail-safe, and refresh signals for every provider. Its store reads go through
/// <see cref="IFactoryCacheStore.TryGetEntryAsync{T}"/>, which single-tier providers do <em>not</em> instrument
/// (that would double-count the coordinator's reads). Direct <see cref="ICache"/> operations are metered at each
/// provider (tier <c>l1</c>/<c>l2</c>): reads record <c>headless.cache.requests</c> (<c>get</c>/<c>get_all</c>/
/// <c>exists</c>, hit/miss outcome), removes and upserts record the write counter, evictions the eviction counter —
/// so cache-aside usage shows read volume and hit rate without the coordinator. The hybrid cache instruments its
/// own per-tier store layer deliberately for the factory path — that is the source of the
/// <c>headless.cache.tier</c> attribution; its direct reads probe the composed L1 provider's public surface, so
/// those probes are attributed to the L1 instance's own cache name.
/// </para>
/// </remarks>
[PublicAPI]
public static class CachingDiagnostics
{
    /// <summary>The full activity-source / meter name used by the caching subsystem (<c>Headless.Caching</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "Caching";

    /// <summary>The <c>headless.cache.name</c> value used for the unkeyed default cache instance.</summary>
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
