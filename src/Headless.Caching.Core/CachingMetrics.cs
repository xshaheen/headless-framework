// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Typed metric instruments for the caching subsystem, registered against
/// <see cref="CachingDiagnostics.Meter"/>. Framework-owned dimensions are namespaced <c>headless.cache.*</c> per
/// the OTel conventions (docs/solutions/conventions/opentelemetry-instrumentation-conventions.md); the raw cache
/// key is never a dimension (cardinality + privacy).
/// </summary>
/// <remarks>
/// Instruments are created directly on the <see cref="Meter"/> (rather than through the
/// <c>Microsoft.Extensions.Telemetry</c> source generator that distributed-locks uses) for two reasons: the
/// generated wrapper hides the underlying <c>Counter.Enabled</c>/<c>Histogram.Enabled</c> flag that the hot-path
/// early-out needs (each record helper short-circuits before building a <see cref="TagList"/> when no listener is
/// attached), and the factory-duration histogram requires a <c>unit: "ms"</c> in its metadata.
/// </remarks>
internal static class CachingMetrics
{
    // --- Instrument names -------------------------------------------------------------------------------------

    internal const string RequestsName = "headless.cache.requests";
    internal const string WritesName = "headless.cache.writes";
    internal const string EvictionsName = "headless.cache.evictions";
    internal const string FactoryExecutionsName = "headless.cache.factory.executions";
    internal const string FactoryDurationName = "headless.cache.factory.duration";
    internal const string FailSafeActivationsName = "headless.cache.failsafe.activations";
    internal const string RefreshesName = "headless.cache.refreshes";
    internal const string InvalidationsName = "headless.cache.invalidations";

    // --- Dimension (tag) names --------------------------------------------------------------------------------

    internal const string TagName = "headless.cache.name";
    internal const string TagOperation = "headless.cache.operation";
    internal const string TagOutcome = "headless.cache.outcome";
    internal const string TagTier = "headless.cache.tier";
    internal const string TagEvictReason = "headless.cache.evict_reason";
    internal const string TagTrigger = "headless.cache.trigger";
    internal const string TagRefreshKind = "headless.cache.refresh_kind";
    internal const string TagInvalidationKind = "headless.cache.invalidation_kind";
    internal const string TagDirection = "headless.cache.direction";
    internal const string TagKey = "headless.cache.key";

    // --- Tier values ------------------------------------------------------------------------------------------

    internal const string TierL1 = "l1";
    internal const string TierL2 = "l2";
    internal const string TierHybrid = "hybrid";

    // --- Outcome values ---------------------------------------------------------------------------------------

    internal const string OutcomeHit = "hit";
    internal const string OutcomeMiss = "miss";
    internal const string OutcomeStale = "stale";
    internal const string OutcomeSuccess = "success";
    internal const string OutcomeError = "error";
    internal const string OutcomeTimeout = "timeout";

    // --- Operation values -------------------------------------------------------------------------------------

    internal const string OperationGetOrAdd = "get_or_add";
    internal const string OperationGet = "get";
    internal const string OperationGetAll = "get_all";
    internal const string OperationExists = "exists";
    internal const string OperationUpsert = "upsert";
    internal const string OperationRemove = "remove";
    internal const string OperationRemoveByPrefix = "remove_by_prefix";
    internal const string OperationRemoveByTag = "remove_by_tag";

    // --- Evict-reason values ----------------------------------------------------------------------------------

    internal const string EvictExpired = "expired";
    internal const string EvictCapacity = "capacity";
    internal const string EvictRemoved = "removed";
    internal const string EvictFlushed = "flushed";

    // --- Fail-safe trigger values -----------------------------------------------------------------------------

    internal const string TriggerFactoryError = "factory_error";
    internal const string TriggerFactoryTimeout = "factory_timeout";
    internal const string TriggerLockAcquireFailed = "lock_acquire_failed";

    // --- Refresh-kind values ----------------------------------------------------------------------------------

    internal const string RefreshEager = "eager";
    internal const string RefreshBackground = "background";

    // --- Invalidation-kind values -----------------------------------------------------------------------------

    internal const string InvalidationTag = "tag";
    internal const string InvalidationClear = "clear";
    internal const string InvalidationFlush = "flush";

    // --- Direction values -------------------------------------------------------------------------------------

    internal const string DirectionPublish = "publish";
    internal const string DirectionReceive = "receive";

    // --- Instruments ------------------------------------------------------------------------------------------

    private static readonly Counter<long> _Requests = CachingDiagnostics.Meter.CreateCounter<long>(
        RequestsName,
        description: "Cache read outcomes (hit/miss/stale)."
    );

    private static readonly Counter<long> _Writes = CachingDiagnostics.Meter.CreateCounter<long>(
        WritesName,
        description: "Cache set/upsert operations."
    );

    private static readonly Counter<long> _Evictions = CachingDiagnostics.Meter.CreateCounter<long>(
        EvictionsName,
        description: "Cache entry evictions."
    );

    private static readonly Counter<long> _FactoryExecutions = CachingDiagnostics.Meter.CreateCounter<long>(
        FactoryExecutionsName,
        description: "Factory executions; the failure-rate denominator."
    );

    private static readonly Histogram<double> _FactoryDuration = CachingDiagnostics.Meter.CreateHistogram<double>(
        FactoryDurationName,
        unit: "ms",
        description: "Factory execution latency in milliseconds."
    );

    private static readonly Counter<long> _FailSafeActivations = CachingDiagnostics.Meter.CreateCounter<long>(
        FailSafeActivationsName,
        description: "Fail-safe stale serving activations."
    );

    private static readonly Counter<long> _Refreshes = CachingDiagnostics.Meter.CreateCounter<long>(
        RefreshesName,
        description: "Eager and background refresh outcomes."
    );

    private static readonly Counter<long> _Invalidations = CachingDiagnostics.Meter.CreateCounter<long>(
        InvalidationsName,
        description: "Hybrid tag/clear/flush invalidation propagation."
    );

    /// <summary>Whether the evictions counter has a subscribed listener (used to gate O(n) count reads).</summary>
    internal static bool EvictionsEnabled => _Evictions.Enabled;

    /// <summary>Whether any caching instrument currently has a subscribed listener.</summary>
    internal static bool AnyEnabled =>
        _Requests.Enabled
        || _Writes.Enabled
        || _Evictions.Enabled
        || _FactoryExecutions.Enabled
        || _FactoryDuration.Enabled
        || _FailSafeActivations.Enabled
        || _Refreshes.Enabled
        || _Invalidations.Enabled;

    // --- Record helpers ---------------------------------------------------------------------------------------

    internal static void RecordRequest(string cacheName, string operation, string outcome, string tier, long count = 1)
    {
        if (count <= 0 || !_Requests.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagName, cacheName },
            { TagOperation, operation },
            { TagOutcome, outcome },
            { TagTier, tier },
        };

        _Requests.Add(count, tags);
    }

    internal static void RecordWrite(string cacheName, string operation, string tier, long count = 1)
    {
        if (count <= 0 || !_Writes.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagName, cacheName },
            { TagOperation, operation },
            { TagTier, tier },
        };

        _Writes.Add(count, tags);
    }

    internal static void RecordEviction(string cacheName, string evictReason, string tier, long count = 1)
    {
        if (count <= 0 || !_Evictions.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagName, cacheName },
            { TagEvictReason, evictReason },
            { TagTier, tier },
        };

        _Evictions.Add(count, tags);
    }

    internal static void RecordFactoryExecution(string cacheName, string outcome)
    {
        if (!_FactoryExecutions.Enabled)
        {
            return;
        }

        var tags = new TagList { { TagName, cacheName }, { TagOutcome, outcome } };
        _FactoryExecutions.Add(1, tags);
    }

    internal static void RecordFactoryDuration(string cacheName, string outcome, double milliseconds)
    {
        if (!_FactoryDuration.Enabled)
        {
            return;
        }

        var tags = new TagList { { TagName, cacheName }, { TagOutcome, outcome } };
        _FactoryDuration.Record(milliseconds, tags);
    }

    internal static void RecordFailSafeActivation(string cacheName, string trigger)
    {
        if (!_FailSafeActivations.Enabled)
        {
            return;
        }

        var tags = new TagList { { TagName, cacheName }, { TagTrigger, trigger } };
        _FailSafeActivations.Add(1, tags);
    }

    internal static void RecordRefresh(string cacheName, string refreshKind, string outcome)
    {
        if (!_Refreshes.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagName, cacheName },
            { TagRefreshKind, refreshKind },
            { TagOutcome, outcome },
        };

        _Refreshes.Add(1, tags);
    }

    internal static void RecordInvalidation(string cacheName, string invalidationKind, string direction)
    {
        if (!_Invalidations.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { TagName, cacheName },
            { TagInvalidationKind, invalidationKind },
            { TagDirection, direction },
        };

        _Invalidations.Add(1, tags);
    }
}
