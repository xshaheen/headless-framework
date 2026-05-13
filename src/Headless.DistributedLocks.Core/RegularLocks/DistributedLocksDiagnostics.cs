// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal static class DistributedLocksDiagnostics
{
    public const string SourceName = HeadlessDiagnostics.Prefix + "DistributedLocks";

    public static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("DistributedLocks");

    public static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("DistributedLocks");

    public static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}
