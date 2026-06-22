// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Centralises the <see cref="ActivitySource"/> and <see cref="Meter"/> instances used by
/// the distributed-locks subsystem. All providers share these singletons so traces and
/// metrics land in a single named scope (<c>Headless.DistributedLocks</c>).
/// </summary>
internal static class DistributedLocksDiagnostics
{
    /// <summary>The full activity-source / meter name used by the distributed-locks subsystem.</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "DistributedLocks";

    /// <summary>Shared <see cref="ActivitySource"/> for distributed-lock traces.</summary>
    public static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("DistributedLocks");

    /// <summary>Shared <see cref="Meter"/> for distributed-lock metrics.</summary>
    public static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("DistributedLocks");

    /// <summary>
    /// Starts a new <see cref="Activity"/> with the given <paramref name="name"/> if a listener is
    /// attached, otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="name">The activity operation name (e.g. <c>"lock.acquire"</c>).</param>
    /// <param name="kind">The activity kind; defaults to <see cref="ActivityKind.Internal"/>.</param>
    /// <returns>The started activity, or <see langword="null"/> when no listener is subscribed.</returns>
    public static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}
