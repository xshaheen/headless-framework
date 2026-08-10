// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>Framework-level defaults for cron misfire detection and recovery.</summary>
[PublicAPI]
public static class JobsRecoveryDefaults
{
    /// <summary>
    /// Seconds of lateness tolerated before a single pending occurrence counts as a misfire.
    /// </summary>
    /// <remarks>
    /// Matches Quartz.NET's misfire threshold. A grace of zero is not a meaningful setting: every dispatch is
    /// necessarily at or after its scheduled instant, so a zero threshold would make an ordinary tick delayed by a
    /// garbage collection or a thread-pool stall indistinguishable from a genuine misfire and route routine work into
    /// recovery. A definition persisting zero is therefore treated as "not yet resolved" and falls back to this value.
    /// </remarks>
    public const int MissedRunGraceSeconds = 60;

    /// <summary>
    /// Maximum occurrences enumerated when counting a backlog, after which the count is reported as a lower bound.
    /// </summary>
    /// <remarks>
    /// Bounds reporting only — never the decision. Whether a definition enters recovery is known after at most two
    /// evaluations (more than one pending instant, or one older than the grace threshold), so the ceiling can never
    /// change the outcome. It exists because a seconds-resolution schedule after a long outage would otherwise walk
    /// millions of instants to produce a number nothing acts on.
    /// </remarks>
    public const int EvaluationCeiling = 1000;
}
