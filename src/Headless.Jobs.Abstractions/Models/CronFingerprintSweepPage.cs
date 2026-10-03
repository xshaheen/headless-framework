// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>Provider-owned sweep page and activation snapshot boundary.</summary>
[PublicAPI]
public sealed record CronFingerprintSweepPage
{
    public required IReadOnlyList<CronDispatchCandidate> Candidates { get; init; }
    public required DateTime StoreUtcNow { get; init; }
    public Guid? SnapshotHighWatermarkId { get; init; }
    public required bool HasMore { get; init; }

    /// <summary>
    /// Whether this page consumed the pass's one bounded wrap opportunity after exhausting the forward range. A
    /// caller must end that pass after this page and begin the next pass from a fresh snapshot.
    /// </summary>
    public bool Wrapped { get; init; }
}
