// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Metric instruments for the Jobs subsystem, registered against <see cref="JobsDiagnostics.Meter" />. There is no
/// OpenTelemetry semantic convention for job scheduling, so instrument and attribute names use the bespoke
/// <c>headless.jobs.*</c> namespace (see docs/solutions/conventions/opentelemetry-instrumentation-conventions.md).
/// </summary>
internal static class JobsMetrics
{
    /// <summary>Coordinated post-commit signals dropped before the worker could run them.</summary>
    internal const string PostCommitSignalsDroppedName = "headless.jobs.post_commit_signals.dropped";

    /// <summary>Why a signal was dropped: <c>full</c> (channel at capacity) or <c>stopping</c> (host shutting down).</summary>
    internal const string TagDropReason = "headless.jobs.drop_reason";

    /// <summary>Due cron occurrences recorded as skipped instead of running.</summary>
    internal const string CronOccurrencesSkippedName = "headless.jobs.cron.occurrences.skipped";

    /// <summary>
    /// Why the occurrences were skipped: <c>overlap</c> (an earlier occurrence was still unfinished) or
    /// <c>missed_run</c> (a missed-run recovery retired them).
    /// </summary>
    internal const string TagSkipReason = "headless.jobs.skip_reason";

    /// <summary>The cron function whose occurrences were skipped. Bounded by the registered function set.</summary>
    internal const string TagFunction = "headless.jobs.function";

    internal const string SkipReasonOverlap = "overlap";

    internal const string SkipReasonMissedRun = "missed_run";

    private static readonly Counter<long> _CronOccurrencesSkipped = JobsDiagnostics.Meter.CreateCounter<long>(
        CronOccurrencesSkippedName,
        unit: "{occurrence}",
        description: "Due cron occurrences the scheduler recorded as skipped instead of running, by reason."
    );

    private static readonly Counter<long> _PostCommitSignalsDropped = JobsDiagnostics.Meter.CreateCounter<long>(
        PostCommitSignalsDroppedName,
        unit: "{signal}",
        description: "Coordinated post-commit signals dropped before the worker ran them; the poll sweep recovers the row."
    );

    internal static void PostCommitSignalDropped(string reason)
    {
        if (!_PostCommitSignalsDropped.Enabled)
        {
            return;
        }

        _PostCommitSignalsDropped.Add(1, new TagList { { TagDropReason, reason } });
    }

    internal static void CronOccurrencesSkipped(string reason, string function, int count)
    {
        if (count <= 0 || !_CronOccurrencesSkipped.Enabled)
        {
            return;
        }

        _CronOccurrencesSkipped.Add(count, new TagList { { TagSkipReason, reason }, { TagFunction, function } });
    }
}
