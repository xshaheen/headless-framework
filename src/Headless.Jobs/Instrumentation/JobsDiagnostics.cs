// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Jobs.Internal;

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

    /// <summary>Name of the producer span that wraps every write that schedules a job run.</summary>
    internal const string ScheduleActivityName = "job.schedule";

    internal static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind, default(ActivityContext));
    }

    /// <summary>
    /// Starts the producer span for a write that schedules a run, as a child of the caller's ambient span. Its
    /// context, or the ambient one when no listener created it, is what the row stores as its <c>TraceParent</c>.
    /// </summary>
    internal static Activity? StartSchedule(JobType type, string? function)
    {
        var activity = ActivitySource.StartActivity(ScheduleActivityName, ActivityKind.Producer);

        if (activity is not null)
        {
            activity.SetTag("headless.job.type", type.ToString());
            if (function is not null)
            {
                activity.SetTag("headless.job.function", function);
            }
        }

        return activity;
    }

    /// <summary>
    /// Starts an execution span as the root of a new trace, linked to the persisted scheduling context when
    /// <paramref name="traceParent"/> holds one. The caller must clear <see cref="Activity.Current"/> first, since a
    /// default parent context falls back to it; a run is its own trace rather than part of the poll loop's.
    /// </summary>
    internal static Activity? StartExecution(string name, string? traceParent)
    {
        return JobTraceParent.TryGetLink(traceParent, out var link)
            ? ActivitySource.StartActivity(name, ActivityKind.Internal, default(ActivityContext), links: [link])
            : ActivitySource.StartActivity(name, ActivityKind.Internal, default(ActivityContext));
    }
}
