// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// How a cron definition recovers when its schedule watermark falls behind — more than one occurrence is
/// pending, or a single pending occurrence is older than the definition's grace threshold.
/// </summary>
/// <remarks>
/// Bounded catch-up, which would replay more than one missed occurrence, is deliberately not offered. Quartz
/// has shipped only fire-once and do-nothing for cron triggers for two decades, systemd runs exactly one
/// catch-up, and Hangfire's per-occurrence <c>Strict</c> mode is not its default.
/// </remarks>
[PublicAPI]
public enum MissedRunPolicy
{
    /// <summary>
    /// Default. Materializes exactly one run for a recovery regardless of how many occurrences were missed. The
    /// run reports the first unaccounted-for missed instant as its scheduled instant.
    /// </summary>
    Coalesce = 0,

    /// <summary>
    /// Advances the watermark past every missed occurrence without materializing a run. A not-yet-executing
    /// occurrence already sitting at a missed instant is transitioned to skipped rather than left to execute.
    /// </summary>
    Skip = 1,
}
