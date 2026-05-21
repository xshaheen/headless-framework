// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

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
    public double PollingCadenceFraction { get; set; } = 0.5;

    /// <summary>Fraction of the lease TTL used as the polling cadence when auto-extending leases.</summary>
    public double AutoExtensionCadenceFraction { get; set; } = 1.0 / 3.0;
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
    }
}
