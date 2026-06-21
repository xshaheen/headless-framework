// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Headless.Testing.Helpers;

/// <summary>
/// <see cref="IClock"/> implementation backed by a <see cref="TimeProvider"/> for use in tests.
/// Defaults to a new <see cref="FakeTimeProvider"/> when none is supplied, enabling deterministic,
/// manually-advanceable time in unit tests.
/// </summary>
/// <remarks>
/// Use <c>AddTestTimeProvider</c> to register this
/// clock alongside a shared <see cref="FakeTimeProvider"/> in a DI container, then advance time via
/// the returned <see cref="FakeTimeProvider"/> instance.
/// </remarks>
/// <param name="timeProvider">
/// The time provider to delegate to. When <see langword="null"/>, a new <see cref="FakeTimeProvider"/>
/// is created automatically.
/// </param>
[PublicAPI]
public sealed class TestClock(TimeProvider? timeProvider = null) : IClock
{
    /// <summary>The underlying <see cref="TimeProvider"/> this clock delegates to.</summary>
    public TimeProvider TimeProvider { get; init; } = timeProvider ?? new FakeTimeProvider();

    /// <inheritdoc/>
    public TimeZoneInfo LocalTimeZone => TimeProvider.LocalTimeZone;

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => TimeProvider.GetUtcNow();

    /// <inheritdoc/>
    public DateTimeOffset LocalNow => TimeProvider.GetLocalNow();

    /// <inheritdoc/>
    public long GetTimestamp() => TimeProvider.GetTimestamp();

    /// <inheritdoc/>
    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        return TimeProvider.GetElapsedTime(startingTimestamp, endingTimestamp);
    }

    /// <inheritdoc/>
    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return TimeProvider.GetElapsedTime(startingTimestamp);
    }

    /// <summary>Normalizes a <see cref="DateTimeOffset"/> to UTC.</summary>
    /// <param name="v">The value to normalize.</param>
    /// <returns>The equivalent UTC <see cref="DateTimeOffset"/>.</returns>
    public DateTimeOffset Normalize(DateTimeOffset v) => v.ToUniversalTime();

    /// <summary>
    /// Normalizes a <see cref="DateTime"/> to UTC. <see cref="DateTimeKind.Local"/> values are
    /// converted; <see cref="DateTimeKind.Unspecified"/> values are stamped as UTC without conversion
    /// (assumed to already represent UTC).
    /// </summary>
    /// <param name="v">The value to normalize.</param>
    /// <returns>A <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>.</returns>
    public DateTime Normalize(DateTime v)
    {
        // Normalizes to UTC. Unspecified is assumed to already be UTC and is stamped without conversion.
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
    }
}
