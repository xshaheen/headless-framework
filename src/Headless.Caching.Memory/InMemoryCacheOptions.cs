// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Caching;

[PublicAPI]
public sealed class InMemoryCacheOptions : CacheOptions
{
    /// <summary>Gets or sets the maximum number of items to store in the cache.</summary>
    public int? MaxItems { get; set; } = 10000;

    /// <summary>Gets or sets a value indicating whether values should be cloned during get and set to make sure that any cache entry changes are isolated.</summary>
    public bool CloneValues { get; set; }

    /// <summary>Maximum memory size in bytes. Requires SizeCalculator.</summary>
    public long? MaxMemorySize { get; set; }

    /// <summary>Function to calculate size of cached objects in bytes. Required when using MaxMemorySize or MaxEntrySize.</summary>
    public Func<object?, long>? SizeCalculator { get; set; }

    /// <summary>Maximum size of a single cache entry in bytes. Requires SizeCalculator.</summary>
    public long? MaxEntrySize { get; set; }

    /// <summary>If true, throws when entry exceeds MaxEntrySize. If false, logs warning and skips.</summary>
    public bool ShouldThrowOnMaxEntrySizeExceeded { get; set; }

    /// <summary>If true, throws on serialization errors during clone. Default: true.</summary>
    public bool ShouldThrowOnSerializationError { get; set; } = true;

    /// <summary>Gets or sets the interval between background maintenance runs. Default: 250ms.</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets or sets the maximum number of items to evict per compaction cycle. Default: 10.</summary>
    public int MaxEvictionsPerCompaction { get; set; } = 10;

    /// <summary>Gets or sets the number of entries to sample when finding eviction candidates. Default: 5.</summary>
    public int EvictionSampleSize { get; set; } = 5;

    /// <summary>Gets or sets the window within which items are considered "hot" and skipped during maintenance. Default: 300ms.</summary>
    public TimeSpan HotAccessWindow { get; set; } = TimeSpan.FromMilliseconds(300);
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
        RuleFor(x => x.HotAccessWindow).GreaterThan(TimeSpan.Zero);
    }
}
