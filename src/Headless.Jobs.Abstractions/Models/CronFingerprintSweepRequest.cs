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
