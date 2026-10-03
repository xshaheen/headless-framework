// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>Outcome of one bounded reconciliation page.</summary>
[PublicAPI]
public sealed record CronFingerprintSweepResult
{
    public required int Scanned { get; init; }
    public required int Rebased { get; init; }
    public required int Deferred { get; init; }
    public required int LostFence { get; init; }
    public required bool HasMore { get; init; }

    /// <summary>
    /// Definitions this host could not evaluate and therefore left untouched — currently a timezone identifier the
    /// running host's timezone database has no entry for.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Deferred"/>, and the distinction is the point: a deferred definition is invalid on
    /// every host and is quarantined for the whole fleet, while these are node-local and write nothing durable, so
    /// peers keep scheduling them. A count that stays non-zero on one node while its peers report zero says this
    /// host's timezone data is behind, not that the definition is broken.
    /// </remarks>
    public int SkippedNodeLocal { get; init; }

    /// <inheritdoc cref="CronFingerprintSweepPage.Wrapped"/>
    public bool Wrapped { get; init; }
    public Guid? NextCursorId { get; init; }
    public Guid? SnapshotHighWatermarkId { get; init; }
}
