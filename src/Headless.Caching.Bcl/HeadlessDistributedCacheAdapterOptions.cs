// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

/// <summary>Defines options for the Headless <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> adapter.</summary>
/// <remarks>
/// The adapter stores byte array payloads directly without applying a serializer. The named cache instance is
/// isolated from the default <see cref="ICache"/> registration.
/// </remarks>
[PublicAPI]
public sealed class HeadlessDistributedCacheAdapterOptions
{
    /// <summary>Represents the default named cache instance name used by the BCL distributed cache adapter.</summary>
    public const string DefaultCacheName = "bcl-distributed-cache";

    /// <summary>
    /// Gets or sets the named cache instance resolved by the adapter.
    /// Defaults to <see cref="DefaultCacheName"/>.
    /// </summary>
    public string CacheName { get; set; } = DefaultCacheName;

    /// <summary>
    /// Gets or sets the absolute lifetime cap applied when a caller provides only a sliding expiration
    /// or no expiration. Defaults to 1 day.
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
