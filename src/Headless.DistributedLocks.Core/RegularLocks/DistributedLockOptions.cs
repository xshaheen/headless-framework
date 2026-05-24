// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

[PublicAPI]
public sealed class DistributedLockOptions
{
    /// <summary>Resource lock key prefix.</summary>
    public string KeyPrefix { get; set; } = "distributed-lock:";

    /// <summary>Maximum length of resource name. Default: 1024.</summary>
    public int MaxResourceNameLength { get; set; } = 1024;

    /// <summary>Maximum concurrent waiting resources (unique keys). Default: 10,000.</summary>
    public int? MaxConcurrentWaitingResources { get; set; } = 10_000;

    /// <summary>Maximum concurrent waiters per resource. Default: 1,000.</summary>
    public int? MaxWaitersPerResource { get; set; } = 1_000;

    /// <summary>Fraction of the lease TTL used as the polling cadence when validating without renewal.</summary>
    /// <remarks>
    /// Default is 0.5 (½ TTL) — the safety-net cadence for lease validation when the outbox fast-path
    /// is unavailable. The validator allows tuning <em>downward</em> to 0.1 (1/10 TTL) for workloads
    /// that need lower lease-loss detection latency at the cost of more frequent storage polls. The
    /// 0.5 ceiling is the design's slowest acceptable cadence: any slower and a lost lease could go
    /// undetected for longer than the lease itself.
    /// </remarks>
    public double PollingCadenceFraction { get; set; } = 0.5;

    /// <summary>Fraction of the lease TTL used as the polling cadence when auto-extending leases.</summary>
    /// <remarks>
    /// Maximum allowed value is 0.5 so at least two extensions occur per lease window — keeps
    /// the safety net effective under cadence jitter.
    /// </remarks>
    public double AutoExtensionCadenceFraction { get; set; } = 1.0 / 3.0;

    /// <summary>
    /// TTL applied to the reader-writer writer-waiting marker planted by a queued writer
    /// (reader-writer locks only).
    /// </summary>
    /// <remarks>
    /// The marker is the placeholder Redis key value that enforces writer-preference: while it is
    /// present, new readers refuse to acquire so the queued writer eventually promotes. The marker
    /// is short-lived by design — a queued writer that is cancelled or crashes should not strand
    /// readers for the full lease window. Default is 30s. Validated to fall in
    /// <c>(0, 5min]</c>; pick a value larger than your worst-case writer acquire wait but small
    /// enough that an abandoned marker does not keep new readers blocked for long.
    /// </remarks>
    public TimeSpan WriterWaitingMarkerTtl { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class DistributedLockOptionsValidator : AbstractValidator<DistributedLockOptions>
{
    public DistributedLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.MaxResourceNameLength).InclusiveBetween(1, 10_000);
        RuleFor(x => x.MaxConcurrentWaitingResources).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.MaxWaitersPerResource).InclusiveBetween(1, 100_000);
        RuleFor(x => x.PollingCadenceFraction).InclusiveBetween(0.1, 0.5);
        RuleFor(x => x.AutoExtensionCadenceFraction).InclusiveBetween(0.1, 0.5);
        RuleFor(x => x.WriterWaitingMarkerTtl)
            .GreaterThan(TimeSpan.Zero)
            .LessThanOrEqualTo(TimeSpan.FromMinutes(5));
    }
}
