// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

/// <summary>Configuration options for <see cref="InMemoryCache"/>.</summary>
[PublicAPI]
public sealed class InMemoryCacheOptions : CacheOptions
{
    /// <summary>
    /// Gets or sets the maximum number of items the cache may hold before triggering LRU eviction.
    /// <see langword="null"/> disables the item-count cap. Default: 10 000.
    /// </summary>
    public int? MaxItems { get; set; } = 10000;

    /// <summary>
    /// Gets or sets whether retrieved and stored values are deep-cloned (via JSON round-trip) so that
    /// mutations to the caller's copy do not corrupt the cached entry and vice versa. Adds serialization
    /// overhead on every read and write; enable only when cached objects are mutable and shared.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool CloneValues { get; set; }

    /// <summary>
    /// Gets or sets the maximum total memory footprint in bytes for all cached entries combined.
    /// Requires <see cref="SizeCalculator"/> to be set. <see langword="null"/> disables the memory cap.
    /// When the cap is exceeded, LRU eviction removes entries until the cache fits within the limit.
    /// </summary>
    public long? MaxMemorySize { get; set; }

    /// <summary>
    /// Gets or sets the function that returns the estimated size of a cached value in bytes. Required when
    /// <see cref="MaxMemorySize"/> or <see cref="MaxEntrySize"/> is set; the cache constructor throws
    /// <see cref="ArgumentException"/> if either cap is configured without a calculator.
    /// </summary>
    public Func<object?, long>? SizeCalculator { get; set; }

    /// <summary>
    /// Gets or sets the maximum size in bytes for a single cache entry. Requires <see cref="SizeCalculator"/>.
    /// An entry exceeding this limit is rejected: by default it is silently skipped; set
    /// <see cref="ShouldThrowOnMaxEntrySizeExceeded"/> to <see langword="true"/> to throw instead.
    /// </summary>
    public long? MaxEntrySize { get; set; }

    /// <summary>
    /// Gets or sets whether to throw <see cref="MaxEntrySizeExceededException"/> when a write is rejected
    /// because its size exceeds <see cref="MaxEntrySize"/>. Default: <see langword="false"/> (log and skip).
    /// </summary>
    public bool ShouldThrowOnMaxEntrySizeExceeded { get; set; }

    /// <summary>
    /// Gets or sets whether a deserialization error during a clone (when <see cref="CloneValues"/> is
    /// <see langword="true"/>) is re-thrown to the caller. Default: <see langword="true"/> (throw).
    /// Set to <see langword="false"/> to treat a clone failure as a miss and log at warning level instead.
    /// </summary>
    public bool ShouldThrowOnSerializationError { get; set; } = true;

    /// <summary>
    /// Gets or sets the interval between background maintenance passes that reap physically-expired entries
    /// and enforce capacity limits. Shorter intervals reduce peak memory at the cost of more background CPU.
    /// Default: 250 ms.
    /// </summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets the maximum number of entries evicted in a single maintenance compaction cycle when the
    /// cache exceeds its capacity caps. Smaller values keep compaction latency predictable; larger values
    /// reclaim memory faster when the cache is well over capacity. Default: 10.
    /// </summary>
    public int MaxEvictionsPerCompaction { get; set; } = 10;

    /// <summary>
    /// Gets or sets the number of live entries randomly sampled to find the LRU eviction candidate during
    /// each compaction step. A larger sample improves the LRU approximation but increases per-eviction cost.
    /// Default: 5.
    /// </summary>
    public int EvictionSampleSize { get; set; } = 5;
}

internal sealed class InMemoryCacheOptionsValidator : AbstractValidator<InMemoryCacheOptions>
{
    public InMemoryCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull();
        RuleFor(x => x.MaxItems).GreaterThan(0);
        RuleFor(x => x.MaxMemorySize).GreaterThan(0).When(x => x.MaxMemorySize.HasValue);
        RuleFor(x => x.MaxEntrySize).GreaterThan(0).When(x => x.MaxEntrySize.HasValue);
        RuleFor(x => x.MaxEntrySize)
            .LessThanOrEqualTo(x => x.MaxMemorySize)
            .When(x => x.MaxEntrySize.HasValue && x.MaxMemorySize.HasValue);
        RuleFor(x => x.MaintenanceInterval).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.MaxEvictionsPerCompaction).GreaterThan(0);
        RuleFor(x => x.EvictionSampleSize).GreaterThan(0);
    }
}
