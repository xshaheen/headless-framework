// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

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
