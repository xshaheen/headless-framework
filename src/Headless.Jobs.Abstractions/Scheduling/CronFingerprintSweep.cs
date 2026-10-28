// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

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

    /// <summary>
    /// Why the whole pass rebased nothing without reading a page, or <see langword="null"/> when it ran. A deferred
    /// pass reports <see cref="HasMore"/> as <see langword="false"/>, so a drain ends normally; the caller starts a
    /// fresh snapshot on its next pass.
    /// </summary>
    /// <remarks>
    /// Unrelated to <see cref="Deferred"/>, which counts individual definitions quarantined as invalid.
    /// </remarks>
    public CronFingerprintSweepDeferralReason? DeferralReason { get; init; }
}

/// <summary>Why a fingerprint sweep pass rebased nothing.</summary>
[PublicAPI]
public enum CronFingerprintSweepDeferralReason
{
    /// <summary>A live node that runs the sweep published a different cron rules digest than this node.</summary>
    FleetDisagreement = 1,

    /// <summary>The coordination membership snapshot could not be read, so agreement could not be shown.</summary>
    MembershipUnavailable = 2,
}
