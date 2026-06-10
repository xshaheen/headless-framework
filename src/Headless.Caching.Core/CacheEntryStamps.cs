// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Expiration stamps computed from <see cref="CacheEntryOptions"/> for a fresh entry write. This is the single
/// home of the stamp math (fail-safe extends physical retention, eager threshold stamps the eager point, sliding
/// clamps the logical lifetime) so the factory coordinator and the providers' direct
/// <c>UpsertAsync(key, value, options)</c> writes always agree.
/// </summary>
/// <param name="LogicalExpiresAt">The timestamp after which normal reads treat the entry as stale (UTC).</param>
/// <param name="PhysicalExpiresAt">The timestamp after which the entry is no longer retained (UTC).</param>
/// <param name="EagerRefreshAt">The optional timestamp after which a fresh read may trigger an eager background refresh (UTC).</param>
[PublicAPI]
public readonly record struct CacheEntryStamps(
    DateTime LogicalExpiresAt,
    DateTime PhysicalExpiresAt,
    DateTime? EagerRefreshAt
)
{
    /// <summary>Computes the fresh-write stamps for <paramref name="options"/> at <paramref name="now"/>.</summary>
    /// <param name="options">The validated cache entry options.</param>
    /// <param name="now">The current UTC timestamp.</param>
    public static CacheEntryStamps Compute(CacheEntryOptions options, DateTime now)
    {
        var logicalExpiresAt = now.Add(options.Duration);
        var physicalDuration = options.IsFailSafeEnabled
            ? _Max(options.Duration, options.FailSafeMaxDuration)
            : options.Duration;
        var physicalExpiresAt = now.Add(physicalDuration);

        if (options.SlidingExpiration is { } slidingExpiration)
        {
            logicalExpiresAt = _Min(now.Add(slidingExpiration), physicalExpiresAt);
        }

        DateTime? eagerRefreshAt = null;

        if (options.EagerRefreshThreshold is { } eagerRefreshThreshold)
        {
            eagerRefreshAt = now.AddTicks((long)(options.Duration.Ticks * (double)eagerRefreshThreshold));
        }

        return new CacheEntryStamps(logicalExpiresAt, physicalExpiresAt, eagerRefreshAt);
    }

    /// <summary>
    /// Validates <paramref name="options"/> with the rules shared by every factory-backed cache operation and
    /// the options-based direct upsert. Throws before anything is written.
    /// </summary>
    /// <param name="options">The cache entry options to validate.</param>
    public static void ValidateOptions(CacheEntryOptions options)
    {
        Argument.IsPositive(options.Duration);

        if (options.SlidingExpiration is { } configuredSlidingExpiration)
        {
            Argument.IsPositive(configuredSlidingExpiration);

            // Redis encodes the idle window as whole milliseconds; a sub-millisecond span floors to 0 and the
            // frame then decodes as unframed (silent value loss), while in-memory would keep it natively. Reject
            // it at the single sliding write choke point so every provider behaves identically.
            Argument.IsGreaterThanOrEqualTo(configuredSlidingExpiration, TimeSpan.FromMilliseconds(1));

            Ensure.False(
                options.IsFailSafeEnabled,
                "Sliding expiration and fail-safe are not supported together in this version."
            );

            Ensure.False(
                options.EagerRefreshThreshold.HasValue,
                "Sliding expiration and eager refresh are not supported together: both re-arm the logical lifetime."
            );
        }

        if (options.EagerRefreshThreshold is { } eagerRefreshThreshold)
        {
            Argument.IsGreaterThan(eagerRefreshThreshold, 0f, paramName: nameof(options.EagerRefreshThreshold));
            Argument.IsLessThan(eagerRefreshThreshold, 1f, paramName: nameof(options.EagerRefreshThreshold));
        }

        if (options.IsFailSafeEnabled)
        {
            Argument.IsPositive(options.FailSafeMaxDuration);
            Argument.IsPositive(options.FailSafeThrottleDuration);
        }

        _ValidateOptionalTimeout(options.FactorySoftTimeout, nameof(options.FactorySoftTimeout));
        _ValidateOptionalTimeout(options.FactoryHardTimeout, nameof(options.FactoryHardTimeout));
        _ValidateOptionalTimeout(options.BackgroundFactoryCeiling, nameof(options.BackgroundFactoryCeiling));
        _ValidateOptionalTimeout(options.LockTimeout, nameof(options.LockTimeout));

        if (
            options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            && options.FactoryHardTimeout != Timeout.InfiniteTimeSpan
        )
        {
            Argument.IsGreaterThan(
                options.FactoryHardTimeout,
                options.FactorySoftTimeout,
                message: "FactoryHardTimeout must be greater than FactorySoftTimeout when both are finite.",
                paramName: nameof(options.FactoryHardTimeout)
            );
        }

        ValidateTags(options.Tags, paramName: nameof(options.Tags));
    }

    /// <summary>
    /// Validates an invalidation-tag collection: each tag non-empty, and both the count and each tag's UTF-8 byte
    /// length within the u16 limits the provider envelopes encode. Also applied to factory-mutated
    /// <see cref="CacheFactoryContext{T}.Tags"/> at write time, which bypasses options validation.
    /// </summary>
    /// <param name="tags">The tags to validate; <see langword="null"/> is valid (untagged).</param>
    /// <param name="paramName">The parameter name reported on validation failure.</param>
    public static void ValidateTags(IReadOnlyCollection<string>? tags, string paramName = "tags")
    {
        if (tags is null)
        {
            return;
        }

        // The provider envelopes encode the tag count and each tag's UTF-8 byte length as u16; validate at
        // this single choke point so an oversized tag fails fast instead of at the frame codec.
        Argument.IsLessThanOrEqualTo(tags.Count, ushort.MaxValue, paramName: paramName);

        foreach (var tag in tags)
        {
            Argument.IsNotNullOrEmpty(tag, paramName: paramName);
            Argument.IsLessThanOrEqualTo(Encoding.UTF8.GetByteCount(tag), ushort.MaxValue, paramName: paramName);
        }
    }

    /// <summary>
    /// Computes the tags present on <paramref name="previousTags"/> but absent from <paramref name="currentTags"/>
    /// (ordinal). Stores that maintain a reverse tag index use the result to drop stale memberships atomically
    /// with the write. Returns <see langword="null"/> when nothing was dropped.
    /// </summary>
    /// <param name="previousTags">The tags carried by the previous physically-retained entry, if any.</param>
    /// <param name="currentTags">The tags carried by the write replacing it, if any.</param>
    public static IReadOnlyCollection<string>? ComputeRemovedTags(
        IReadOnlyCollection<string>? previousTags,
        IReadOnlyCollection<string>? currentTags
    )
    {
        if (previousTags is not { Count: > 0 })
        {
            return null;
        }

        if (currentTags is not { Count: > 0 })
        {
            return previousTags;
        }

        List<string>? removed = null;

        foreach (var previousTag in previousTags)
        {
            if (!currentTags.Contains(previousTag, StringComparer.Ordinal))
            {
                (removed ??= []).Add(previousTag);
            }
        }

        return removed;
    }

    private static void _ValidateOptionalTimeout(TimeSpan timeout, string paramName)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        Argument.IsPositive(timeout, paramName: paramName);
    }

    private static TimeSpan _Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static DateTime _Min(DateTime left, DateTime right) => left <= right ? left : right;
}
