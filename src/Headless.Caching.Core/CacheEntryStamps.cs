// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
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
/// <param name="CreatedAt">The timestamp at which a fresh value write is created (its birth time, UTC). A re-stamp preserves the source entry's original value instead of using this one.</param>
[PublicAPI]
public readonly record struct CacheEntryStamps(
    DateTime LogicalExpiresAt,
    DateTime PhysicalExpiresAt,
    DateTime? EagerRefreshAt,
    DateTime CreatedAt
)
{
    /// <summary>Computes the fresh-write stamps for <paramref name="options"/> at <paramref name="now"/>.</summary>
    /// <param name="options">The validated cache entry options.</param>
    /// <param name="now">The current UTC timestamp.</param>
    public static CacheEntryStamps Compute(CacheEntryOptions options, DateTime now)
    {
        // A non-positive Duration means the entry is already expired on write (e.g. a BCL absolute expiration in
        // the past): stamp it at `now` so every read treats it as a miss and the provider set paths evict it.
        // Jitter/sliding/eager/fail-safe would re-arm a lifetime the entry never has, so they do not apply.
        if (options.Duration <= TimeSpan.Zero)
        {
            return new CacheEntryStamps(now, now, EagerRefreshAt: null, CreatedAt: now);
        }

        // Anti-stampede jitter: spread mass-expiry by extending Duration by a random [0, JitterMaxDuration). The
        // jittered span MUST be the single Duration source for every derived stamp below (logical, physical, eager)
        // or the physical >= logical invariant breaks for a non-fail-safe entry. When JitterMaxDuration is Zero the
        // jitter is Zero, so effectiveDuration == options.Duration and behavior is unchanged.
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
    /// Validates <paramref name="options"/> with the rules shared by every factory-backed cache operation and
    /// the options-based direct upsert. Throws before anything is written.
    /// </summary>
    /// <param name="options">The cache entry options to validate.</param>
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

    private static long _GetRandomTicks(TimeSpan exclusiveMax) => (long)(exclusiveMax.Ticks * _GetRandomUnitDouble());

    private static double _GetRandomUnitDouble() => RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
}
