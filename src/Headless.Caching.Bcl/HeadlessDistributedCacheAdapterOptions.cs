// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

/// <summary>Options for the Headless <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> adapter.</summary>
/// <remarks>
/// Configure via <c>setup.UseBclCache(…)</c>. The adapter stores <c>byte[]</c> payloads verbatim (the cache's
/// native wire format); no serializer is applied. The named cache instance is separate from the default
/// <see cref="ICache"/> registration so BCL consumers have their own keyspace.
/// </remarks>
[PublicAPI]
public sealed class HeadlessDistributedCacheAdapterOptions
{
    /// <summary>The default named cache instance name used by the BCL distributed-cache adapter.</summary>
    public const string DefaultCacheName = "bcl-distributed-cache";

    /// <summary>
    /// Gets or sets the named Headless cache instance resolved by the adapter. Must not be a reserved cache
    /// provider key (any key in the <c>Headless.Caching:</c> namespace). Defaults to
    /// <see cref="DefaultCacheName"/>.
    /// </summary>
    public string CacheName { get; set; } = DefaultCacheName;

    /// <summary>
    /// Gets or sets the absolute lifetime cap applied when a BCL caller provides only a sliding expiration
    /// or no expiration at all. Must be a positive, finite value. Defaults to 1 day.
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
