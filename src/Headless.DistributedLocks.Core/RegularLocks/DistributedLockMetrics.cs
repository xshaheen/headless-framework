// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.Constants;
using Microsoft.Extensions.Diagnostics.Metrics;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static partial class DistributedLockMetrics
{
    internal static readonly LockFailedCounter LockFailed = Instruments.CreateLockFailedCounter(
        HeadlessDiagnostics.Meter
    );

    internal static readonly LockWaitTimeHistogram LockWaitTime = Instruments.CreateLockWaitTimeHistogram(
        HeadlessDiagnostics.Meter
    );

    private static partial class Instruments
    {
        [Counter<int>(Name = "headless.lock.failed")]
        internal static partial LockFailedCounter CreateLockFailedCounter(Meter meter);

        [Histogram<double>(Name = "headless.lock.wait.time")]
        internal static partial LockWaitTimeHistogram CreateLockWaitTimeHistogram(Meter meter);
    }
}
