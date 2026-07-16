// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Typed metric instruments for the distributed-lock and distributed-semaphore providers.
/// All instruments are registered against <see cref="DistributedLocksDiagnostics.Meter"/>
/// and use the <c>headless.lock.reason</c> / <c>headless.semaphore.reason</c> dimension to
/// distinguish contended vs. stalled failure outcomes (see <see cref="DistributedLockFailureReasons"/>).
/// Framework-owned dimensions are namespaced <c>headless.*</c> per the OTel conventions
/// (docs/solutions/conventions/opentelemetry-instrumentation-conventions.md).
/// </summary>
internal static partial class DistributedLockMetrics
{
    // `reason` dimension values for the `*.failed` counters. Internal aliases of the public
    // `DistributedLockFailureReasons` contract, so provider call sites stay terse while the
    // values have a single public source of truth. `Contended` covers every expected
    // not-acquired outcome (lock held, acquire-timeout elapsed, swallowed transient storage
    // errors); `Stalled` is the non-blocking safety deadline firing (lock-store stall).
    internal const string ReasonContended = DistributedLockFailureReasons.Contended;
    internal const string ReasonStalled = DistributedLockFailureReasons.Stalled;

    internal static readonly LockFailedCounter LockFailed = Instruments.CreateLockFailedCounter(
        DistributedLocksDiagnostics.Meter
    );

    internal static readonly LockWaitTimeHistogram LockWaitTime = Instruments.CreateLockWaitTimeHistogram(
        DistributedLocksDiagnostics.Meter
    );

    internal static readonly SemaphoreFailedCounter SemaphoreFailed = Instruments.CreateSemaphoreFailedCounter(
        DistributedLocksDiagnostics.Meter
    );

    internal static readonly SemaphoreWaitTimeHistogram SemaphoreWaitTime =
        Instruments.CreateSemaphoreWaitTimeHistogram(DistributedLocksDiagnostics.Meter);

    private static partial class Instruments
    {
        [Counter<int>("headless.lock.reason", Name = "headless.lock.failed")]
        internal static partial LockFailedCounter CreateLockFailedCounter(Meter meter);

        [Histogram<double>(Name = "headless.lock.wait.time")]
        internal static partial LockWaitTimeHistogram CreateLockWaitTimeHistogram(Meter meter);

        [Counter<int>("headless.semaphore.reason", Name = "headless.semaphore.failed")]
        internal static partial SemaphoreFailedCounter CreateSemaphoreFailedCounter(Meter meter);

        [Histogram<double>(Name = "headless.semaphore.wait.time")]
        internal static partial SemaphoreWaitTimeHistogram CreateSemaphoreWaitTimeHistogram(Meter meter);
    }
}
