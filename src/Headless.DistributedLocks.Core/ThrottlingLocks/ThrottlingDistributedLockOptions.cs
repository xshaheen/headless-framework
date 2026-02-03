// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class ThrottlingDistributedLockOptions
{
    /// <summary>Resource lock key prefix.</summary>
    public string KeyPrefix { get; set; } = "throttling-lock:";

    public int MaxHitsPerPeriod { get; set; } = 100;

    public TimeSpan ThrottlingPeriod { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed class ThrottlingDistributedLockOptionsValidator : AbstractValidator<ThrottlingDistributedLockOptions>
{
    public ThrottlingDistributedLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull();
        RuleFor(x => x.MaxHitsPerPeriod).GreaterThan(0);
        RuleFor(x => x.ThrottlingPeriod).GreaterThan(TimeSpan.Zero);
    }
}
