// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Maps the <see cref="CachingMetrics"/> string vocabulary (the wire values shared with the metric dimensions) to the
/// typed event enums, in one place, so the event surface and the metrics never drift.
/// </summary>
internal static class CacheEventMappings
{
    public static CacheFactoryOutcome ToFactoryOutcome(string metricOutcome) =>
        metricOutcome switch
        {
            CachingMetrics.OutcomeSuccess => CacheFactoryOutcome.Success,
            CachingMetrics.OutcomeError => CacheFactoryOutcome.Error,
            CachingMetrics.OutcomeTimeout => CacheFactoryOutcome.Timeout,
            _ => CacheFactoryOutcome.Error,
        };

    public static CacheFailSafeTrigger ToFailSafeTrigger(string metricTrigger) =>
        metricTrigger switch
        {
            CachingMetrics.TriggerFactoryError => CacheFailSafeTrigger.FactoryError,
            CachingMetrics.TriggerFactoryTimeout => CacheFailSafeTrigger.FactoryTimeout,
            CachingMetrics.TriggerLockAcquireFailed => CacheFailSafeTrigger.LockAcquireFailed,
            _ => CacheFailSafeTrigger.FactoryError,
        };
}
