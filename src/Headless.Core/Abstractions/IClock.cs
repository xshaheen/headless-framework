// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Abstracts time-related operations so that production code can be tested deterministically by
/// substituting a fake or frozen implementation. Wraps <see cref="TimeProvider"/> semantics and
/// adds normalization helpers for storing timestamps consistently in UTC.
/// </summary>
public interface IClock
{
    /// <summary>Gets the local time zone associated with this clock instance.</summary>
    TimeZoneInfo LocalTimeZone { get; }

    /// <summary>Gets the current date and time expressed as UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Gets the current date and time expressed in <see cref="LocalTimeZone"/>.</summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>
    /// Returns the current high-frequency timestamp value, suitable for measuring elapsed time.
    /// The value is only meaningful when compared against another timestamp obtained from the same clock.
    /// </summary>
    /// <returns>
    /// A <see langword="long"/> timestamp whose unit depends on the underlying <see cref="TimeProvider"/>
    /// frequency. Pass it to <see cref="GetElapsedTime(long, long)"/> or <see cref="GetElapsedTime(long)"/>
    /// to obtain a <see cref="TimeSpan"/>.
    /// </returns>
    long GetTimestamp();

    /// <summary>
    /// Computes the elapsed time between two timestamps previously obtained from <see cref="GetTimestamp"/>.
    /// </summary>
    /// <param name="startingTimestamp">The timestamp recorded at the start of the interval.</param>
    /// <param name="endingTimestamp">The timestamp recorded at the end of the interval.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the elapsed duration.</returns>
    TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp);

    /// <summary>
    /// Computes the elapsed time from a previously obtained timestamp to the current instant.
    /// Equivalent to <c>GetElapsedTime(startingTimestamp, GetTimestamp())</c>.
    /// </summary>
    /// <param name="startingTimestamp">The timestamp recorded at the start of the interval.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the elapsed duration since <paramref name="startingTimestamp"/>.</returns>
    TimeSpan GetElapsedTime(long startingTimestamp);

    /// <summary>
    /// Normalizes a <see cref="DateTimeOffset"/> to UTC by calling <see cref="DateTimeOffset.ToUniversalTime"/>.
    /// Use this before persisting timestamps to ensure a consistent offset.
    /// </summary>
    /// <param name="v">The value to normalize.</param>
    /// <returns>An equivalent <see cref="DateTimeOffset"/> whose offset is zero (UTC).</returns>
    DateTimeOffset Normalize(DateTimeOffset v);

    /// <summary>
    /// Normalizes a <see cref="DateTime"/> to UTC. Local values are converted; values with
    /// <see cref="DateTimeKind.Unspecified"/> are stamped as UTC without conversion (assumed to already be UTC).
    /// Use this before persisting timestamps to ensure a consistent kind.
    /// </summary>
    /// <param name="v">The value to normalize.</param>
    /// <returns>
    /// A <see cref="DateTime"/> with <see cref="DateTimeKind.Utc"/>. Local values are converted via
    /// <see cref="DateTime.ToUniversalTime"/>; unspecified values are re-stamped in place.
    /// </returns>
    DateTime Normalize(DateTime v);
}

/// <summary>
/// Default <see cref="IClock"/> implementation backed by a <see cref="TimeProvider"/> instance.
/// Inject <see cref="TimeProvider.System"/> in production and a fake/frozen provider in tests.
/// </summary>
public sealed class Clock(TimeProvider timeProvider) : IClock
{
    /// <inheritdoc/>
    public TimeZoneInfo LocalTimeZone => timeProvider.LocalTimeZone;

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    /// <inheritdoc/>
    public DateTimeOffset LocalNow => timeProvider.GetLocalNow();

    /// <inheritdoc/>
    public long GetTimestamp() => timeProvider.GetTimestamp();

    /// <inheritdoc/>
    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        return timeProvider.GetElapsedTime(startingTimestamp, endingTimestamp);
    }

    /// <inheritdoc/>
    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return timeProvider.GetElapsedTime(startingTimestamp);
    }

    /// <inheritdoc/>
    public DateTimeOffset Normalize(DateTimeOffset v) => v.ToUniversalTime();

    /// <inheritdoc/>
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
