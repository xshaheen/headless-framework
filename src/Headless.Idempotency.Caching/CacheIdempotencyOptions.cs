// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Caching;

namespace Headless.Idempotency.Caching;

/// <summary>Where the cache-backed idempotency provider keeps its entries.</summary>
[PublicAPI]
public sealed class CacheIdempotencyOptions
{
    /// <summary>
    /// Gets or sets the prefix of every cache key this provider writes: one entry per record plus the generation
    /// counter. Default: <c>headless:idempotency:</c>. Give applications that share one cache distinct prefixes, or
    /// they share records and generations.
    /// </summary>
    public string KeyPrefix { get; set; } = "headless:idempotency:";

    /// <summary>
    /// Gets or sets the name of the keyed <see cref="ICache" /> instance that holds the records, or
    /// <see langword="null" /> (the default) for the application's remote cache: the registered
    /// <see cref="IRemoteCache" /> when there is one, and the default <see cref="ICache" /> otherwise.
    /// </summary>
    /// <remarks>
    /// Name a remote (Redis) instance, not a hybrid one. A hybrid cache can answer a read from a replica's stale local
    /// tier, so every compare-and-swap against the shared tier would keep failing until that tier catches up.
    /// </remarks>
    public string? CacheName { get; set; }
}

internal sealed class CacheIdempotencyOptionsValidator : AbstractValidator<CacheIdempotencyOptions>
{
    public CacheIdempotencyOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
        RuleFor(x => x.CacheName)
            .Must(static name => name is null || !string.IsNullOrWhiteSpace(name))
            .WithMessage("A cache name, when set, must not be empty or whitespace.");
    }
}
