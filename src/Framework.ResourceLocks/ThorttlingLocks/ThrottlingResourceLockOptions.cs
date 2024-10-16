// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public sealed class ThrottlingResourceLockOptions
{
    /// <summary>Resource lock key prefix.</summary>
    public string KeyPrefix { get; set; } = "";

    public int MaxHitsPerPeriod { get; set; } = 100;

    public TimeSpan ThrottlingPeriod { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed class ThrottlingResourceLockOptionsValidator : AbstractValidator<ThrottlingResourceLockOptions>
{
    public ThrottlingResourceLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.MaxHitsPerPeriod).GreaterThan(0);
        RuleFor(x => x.ThrottlingPeriod).GreaterThan(TimeSpan.Zero);
    }
}
