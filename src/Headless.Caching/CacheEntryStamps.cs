// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Caching;

/// <summary>
/// Represents expiration stamps computed from <see cref="CacheEntryOptions"/> for an entry write operation.
/// </summary>
/// <param name="LogicalExpiresAt">The timestamp after which normal reads treat the entry as stale (UTC).</param>
/// <param name="PhysicalExpiresAt">The timestamp after which the entry is no longer retained (UTC).</param>
/// <param name="EagerRefreshAt">The optional timestamp after which a fresh read can trigger an eager background refresh (UTC).</param>
/// <param name="CreatedAt">The timestamp at which the value was created (UTC).</param>
[PublicAPI]
public readonly record struct CacheEntryStamps(
    DateTime LogicalExpiresAt,
    DateTime PhysicalExpiresAt,
    DateTime? EagerRefreshAt,
    DateTime CreatedAt
)
{
    /// <summary>Computes write stamps for <paramref name="options"/> at <paramref name="now"/>.</summary>
    /// <param name="options">The validated cache entry options.</param>
    /// <param name="now">The current UTC timestamp.</param>
    /// <returns>A new <see cref="CacheEntryStamps"/> instance with computed expiration stamps.</returns>
    public static CacheEntryStamps Compute(CacheEntryOptions options, DateTime now)
    {
        // A non-positive Duration indicates immediate expiration. Stamp at current time so reads treat it as a miss.
        if (options.Duration <= TimeSpan.Zero)
        {
            return new CacheEntryStamps(now, now, EagerRefreshAt: null, CreatedAt: now);
        }

        // Anti-stampede jitter: spread mass-expiry by extending Duration by a random interval.
        var effectiveDuration =
            options.JitterMaxDuration > TimeSpan.Zero
                ? options.Duration + TimeSpan.FromTicks(_GetRandomTicks(options.JitterMaxDuration))
                : options.Duration;

        var logicalExpiresAt = now.Add(effectiveDuration);
        var physicalDuration = options.IsFailSafeEnabled
            ? _Max(effectiveDuration, options.FailSafeMaxDuration)
            : effectiveDuration;
        var physicalExpiresAt = now.Add(physicalDuration);

        if (options.SlidingExpiration is { } slidingExpiration)
        {
            logicalExpiresAt = _Min(now.Add(slidingExpiration), physicalExpiresAt);
        }

        DateTime? eagerRefreshAt = null;

        if (options.EagerRefreshThreshold is { } eagerRefreshThreshold)
        {
            eagerRefreshAt = now.AddTicks((long)(effectiveDuration.Ticks * (double)eagerRefreshThreshold));
        }

        return new CacheEntryStamps(logicalExpiresAt, physicalExpiresAt, eagerRefreshAt, CreatedAt: now);
    }

    /// <summary>
    /// Validates <paramref name="options"/> using common caching rules before performing an entry write.
    /// </summary>
    /// <param name="options">The cache entry options to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> contains an invalid duration, threshold, or timeout.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> contains invalid tags or conflicting settings.</exception>
    public static void ValidateOptions(CacheEntryOptions options)
    {
        // Duration is intentionally unconstrained in sign: a non-positive value is a valid "expire immediately"
        // request (Compute stamps it at `now`). The optional-field checks below still reject genuinely
        // contradictory configurations (e.g. sub-millisecond sliding, sliding + fail-safe) regardless of sign.
        Argument.IsPositiveOrZero(options.JitterMaxDuration);

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

        // The lock-holding background-detach path is only reachable when a SOFT timeout is selected, which requires
        // fail-safe enabled AND a finite FactorySoftTimeout (see FactoryCacheCoordinator._SelectFactoryTimeout). On
        // that path an infinite BackgroundFactoryCeiling lets a hung factory hold the per-key lock indefinitely. A
        // finite soft timeout WITHOUT fail-safe is inert (it only logs), so reject only the dangerous combination.
        Ensure.False(
            options.IsFailSafeEnabled
                && options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
                && options.BackgroundFactoryCeiling == Timeout.InfiniteTimeSpan,
            "BackgroundFactoryCeiling must be finite when fail-safe is enabled with a finite FactorySoftTimeout, "
                + "otherwise a hung factory holds the per-key lock indefinitely."
        );

        ValidateTags(options.Tags, paramName: nameof(options.Tags));
    }

    /// <summary>
    /// Validates an invalidation tag collection.
    /// </summary>
    /// <param name="tags">The tags to validate, or <see langword="null"/> when untagged.</param>
    /// <param name="paramName">The parameter name reported on validation failure.</param>
    /// <exception cref="ArgumentException"><paramref name="tags"/> contains an empty tag or exceeds length limits.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tags"/> count exceeds the maximum limit.</exception>
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

    private static void _ValidateOptionalTimeout(TimeSpan timeout, string paramName)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        Argument.IsPositive(timeout, paramName: paramName);
    }

    private static TimeSpan _Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static DateTime _Min(DateTime left, DateTime right)
    {
        return left <= right ? left : right;
    }

    private static long _GetRandomTicks(TimeSpan exclusiveMax)
    {
        return (long)(exclusiveMax.Ticks * _GetRandomUnitDouble());
    }

    // Random.Shared, not a CSPRNG: jitter only desynchronizes expiry, so predictability has no security
    // consequence, and this runs on every jittered write.
#pragma warning disable CA5394 // Non-security cache-expiry jitter; keep Random.Shared on the hot path.
    private static double _GetRandomUnitDouble()
    {
        return Random.Shared.NextDouble();
    }
#pragma warning restore CA5394
}
