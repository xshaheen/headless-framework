// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

/// <summary>Options for the Headless <c>IOutputCacheStore</c> adapter.</summary>
[PublicAPI]
public sealed class HeadlessOutputCacheStoreOptions
{
    /// <summary>The default named cache instance used by the output-cache store adapter.</summary>
    public const string DefaultCacheName = "output-cache";

    /// <summary>
    /// Gets or sets the named Headless cache instance resolved by the store. The name must not be a reserved
    /// cache provider key. Output-cache entries live in their own namespace, isolated from the default cache.
    /// </summary>
    public string CacheName { get; set; } = DefaultCacheName;

    /// <summary>
    /// Gets or sets the expiration applied when ASP.NET hands the store a non-positive <c>validFor</c>. Matches
    /// ASP.NET's 60-second output-cache default; a positive <c>validFor</c> always passes through unchanged.
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(1);
}

internal sealed class HeadlessOutputCacheStoreOptionsValidator : AbstractValidator<HeadlessOutputCacheStoreOptions>
{
    public HeadlessOutputCacheStoreOptionsValidator()
    {
        RuleFor(x => x.CacheName)
            .NotEmpty()
            .Must(static name => !CacheConstants.IsReservedProviderKey(name))
            .WithMessage("The output-cache store cache name must not be a reserved cache provider key.");
        RuleFor(x => x.DefaultExpiration).GreaterThan(TimeSpan.Zero);
    }
}
