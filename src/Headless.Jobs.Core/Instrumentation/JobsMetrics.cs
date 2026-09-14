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
}
