// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

// Instrumentation identity + emission helpers for the get-or-add pipeline: the parent cache.get_or_add span,
// the get-or-add outcome mapping, and the factory-execution / fail-safe metric recorders.
public sealed partial class FactoryCacheCoordinator
{
    // Starts the parent cache.get_or_add span (caller already confirmed a listener is attached), stamping the
    // low-cardinality identity tags. The raw key is attached only under the opt-in IncludeKeyInTraces flag.
    private Activity? _StartGetOrAddActivity(string key)
    {
        var activity = CachingDiagnostics.Start("cache.get_or_add");

        if (activity is null)
        {
            return null;
        }

        activity.SetTag(CachingMetrics.TagName, _cacheName);
        activity.SetTag(CachingMetrics.TagTier, _cacheTier);

        if (includeKeyInTraces)
        {
            activity.SetTag(CachingMetrics.TagKey, key);
        }

        return activity;
    }

    // Maps the returned value + whether the factory ran onto the get-or-add outcome dimension. A stale-served value
    // is `stale`; a value present without the factory running is a fresh `hit`; anything else (factory-computed
    // value or a degraded NoValue miss) is `miss`.
    private static string _ResolveGetOrAddOutcome<T>(CacheValue<T> value, bool reachedFactory)
    {
        if (value.IsStale)
        {
            return CachingMetrics.OutcomeStale;
        }

        return value.HasValue && !reachedFactory ? CachingMetrics.OutcomeHit : CachingMetrics.OutcomeMiss;
    }

    // Records the factory execution count + latency (monotonic, from the injected TimeProvider) and stamps the
    // cache.factory child span's outcome, marking it errored on the error outcome.
    private void _RecordFactoryOutcome(Activity? factoryActivity, long startTimestamp, string outcome)
    {
        CachingMetrics.RecordFactoryExecution(_cacheName, outcome);
        CachingMetrics.RecordFactoryDuration(
            _cacheName,
            outcome,
            _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds
        );

        if (factoryActivity is null)
        {
            return;
        }

        factoryActivity.SetTag(CachingMetrics.TagOutcome, outcome);

        if (string.Equals(outcome, CachingMetrics.OutcomeError, StringComparison.Ordinal))
        {
            factoryActivity.SetStatus(ActivityStatusCode.Error);
        }
    }

    // Records a fail-safe stale-serving activation. Fail-safe is a normal degradation, not an error: it increments
    // the metric and is surfaced on the get-or-add span as an event + attribute, never as span error status.
    private void _RecordFailSafe(Activity? activity, string trigger)
    {
        CachingMetrics.RecordFailSafeActivation(_cacheName, trigger);

        if (activity is null)
        {
            return;
        }

        activity.SetTag("headless.cache.failsafe", value: true);
        activity.AddEvent(
            new ActivityEvent(
                "cache.failsafe",
                tags: new ActivityTagsCollection { { CachingMetrics.TagTrigger, trigger } }
            )
        );
    }
}
