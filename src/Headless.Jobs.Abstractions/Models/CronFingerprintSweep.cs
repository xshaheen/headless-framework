// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>One bounded, keyset-ordered read of cron definitions requiring fingerprint reconciliation.</summary>
[PublicAPI]
public sealed record CronFingerprintSweepRequest
{
    public required IReadOnlyCollection<string> CurrentFingerprints { get; init; }
    public required int Limit { get; init; }
    public Guid? AfterId { get; init; }
    public Guid? ThroughId { get; init; }
    public bool AllowWrap { get; init; }
}

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

/// <summary>Durably defers a deterministic definition-evaluation failure behind its existing position fence.</summary>
[PublicAPI]
public sealed record CronFingerprintDeferRequest
{
    public required Guid CronJobId { get; init; }
    public required long ExpectedScheduleRevision { get; init; }
    public required DateTime ObservedReconciledThroughUtc { get; init; }
    public string? ObservedEvaluationFingerprint { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required TimeSpan MaximumDelay { get; init; }
}

/// <summary>Outcome of one bounded reconciliation page.</summary>
[PublicAPI]
public sealed record CronFingerprintSweepResult
{
    public required int Scanned { get; init; }
    public required int Rebased { get; init; }
    public required int Deferred { get; init; }
    public required int LostFence { get; init; }
    public required bool HasMore { get; init; }

    /// <inheritdoc cref="CronFingerprintSweepPage.Wrapped"/>
    public bool Wrapped { get; init; }
    public Guid? NextCursorId { get; init; }
    public Guid? SnapshotHighWatermarkId { get; init; }
}
