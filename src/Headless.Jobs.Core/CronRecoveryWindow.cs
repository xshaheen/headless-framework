// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>The half-open span of schedule instants a recovery pass reads before it can decide anything.</summary>
[PublicAPI]
public sealed record CronRecoveryWindow
{
    /// <summary>Exclusive lower bound — the watermark the caller observed.</summary>
    public required DateTime StartExclusiveUtc { get; init; }

    /// <summary>
    /// Inclusive upper bound. Shorter than the recovery instant when a saturated coalesce evaluation examined only a
    /// prefix of the elapsed instants: nothing beyond that prefix has been considered, so nothing beyond it may be
    /// read into the decision.
    /// </summary>
    public required DateTime EndInclusiveUtc { get; init; }
}
