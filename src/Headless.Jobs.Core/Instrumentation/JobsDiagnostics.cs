// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Centralises the <see cref="ActivitySource"/> and <see cref="Meter"/> used by the Jobs subsystem. Both carry
/// <see cref="SourceName"/>: consumers subscribe via <c>AddSource</c> / <c>AddMeter</c> with that name, or the typed
/// <c>AddJobsInstrumentation()</c> extension on <c>TracerProviderBuilder</c>.
/// </summary>
[PublicAPI]
public static class JobsDiagnostics
{
    /// <summary>The full activity-source and meter name used by the Jobs subsystem (<c>Headless.Jobs</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "Jobs";

    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Jobs");

    /// <summary>Shared <see cref="Meter"/> for Jobs metrics (see <c>JobsMetrics</c> for the instruments).</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("Jobs");

    internal static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }
}
