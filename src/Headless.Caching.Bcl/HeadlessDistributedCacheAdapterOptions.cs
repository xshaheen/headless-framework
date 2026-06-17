// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

/// <summary>Options for the Headless <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> adapter.</summary>
[PublicAPI]
public sealed class HeadlessDistributedCacheAdapterOptions
{
    /// <summary>The default named cache instance used by the BCL distributed-cache adapter.</summary>
    public const string DefaultCacheName = "bcl-distributed-cache";

    /// <summary>
    /// Gets or sets the named Headless cache instance resolved by the adapter.
    /// The name must not be a reserved cache provider key.
    /// </summary>
    public string CacheName { get; set; } = DefaultCacheName;

    /// <summary>
    /// Gets or sets the absolute lifetime cap used when BCL callers provide only sliding expiration, or no
    /// expiration options at all.
    /// </summary>
    public TimeSpan DefaultAbsoluteExpiration { get; set; } = TimeSpan.FromDays(1);
}

internal sealed class HeadlessDistributedCacheAdapterOptionsValidator
    : AbstractValidator<HeadlessDistributedCacheAdapterOptions>
{
    public HeadlessDistributedCacheAdapterOptionsValidator()
    {
        RuleFor(x => x.CacheName)
            .NotEmpty()
            .Must(static name => !CacheConstants.IsReservedProviderKey(name))
            .WithMessage("The BCL distributed-cache adapter cache name must not be a reserved cache provider key.");
        RuleFor(x => x.DefaultAbsoluteExpiration).GreaterThan(TimeSpan.Zero);
    }
}
