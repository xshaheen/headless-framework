// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

/// <summary>Validates <see cref="HybridCacheOptions"/>.</summary>
public sealed class HybridCacheOptionsValidator : AbstractValidator<HybridCacheOptions>
{
    public HybridCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull().WithMessage("KeyPrefix cannot be null");

        RuleFor(x => x.DefaultLocalExpiration)
            .Must(x => x is null || x.Value > TimeSpan.Zero)
            .WithMessage("DefaultLocalExpiration must be positive if set");

        RuleFor(x => x.LocalCacheName)
            .Must(x => x is null || !string.IsNullOrWhiteSpace(x))
            .WithMessage("LocalCacheName must be non-empty if set");

        RuleFor(x => x.RemoteCacheName)
            .Must(x => x is null || !string.IsNullOrWhiteSpace(x))
            .WithMessage("RemoteCacheName must be non-empty if set");
    }
}
