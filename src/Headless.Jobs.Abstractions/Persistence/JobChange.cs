// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.Jobs;

/// <summary>One Jobs row whose change marker a change read returned.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobChange
{
    /// <summary>Gets the table the row lives in.</summary>
    public required JobChangeKind Kind { get; init; }

    /// <summary>Gets the row's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the row's change marker, <see cref="BaseJobEntity.ChangedAt"/>, as the read saw it.</summary>
    public required DateTime ChangedAt { get; init; }

    /// <summary>
    /// Gets the row's version: its change marker in ticks. Every write to the row raises it, so a reader that already
    /// handled a version of a row can drop a repeat.
    /// </summary>
    public long Version => ChangedAt.Ticks;
}

/// <summary>The Jobs table a changed row lives in.</summary>
[PublicAPI]
#pragma warning disable CA1008 // Every changed row lives in a table, so a zero member would name none.
public enum JobChangeKind
{
    /// <summary>A time job, a root or a chain member.</summary>
    TimeJob = 1,

    /// <summary>A cron job definition.</summary>
    CronJob = 2,

    /// <summary>One occurrence (run) of a cron job.</summary>
    CronJobOccurrence = 3,
}
#pragma warning restore CA1008

/// <summary>The keyset position of one row in a change read: its change marker, then its identifier.</summary>
/// <remarks>
/// The identifier breaks ties between rows one statement stamped with the same marker. Stores order identifiers
/// differently, so a position is meaningful only to the store that produced it.
/// </remarks>
/// <param name="ChangedAt">The row's change marker.</param>
/// <param name="Id">The row's identifier.</param>
[PublicAPI]
public readonly record struct JobChangePosition(DateTime ChangedAt, Guid Id);

/// <summary>One bounded change read: the changed rows and the cursor to read from next.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobChangePage
{
    /// <summary>Gets the changed rows, in change-marker order within each table.</summary>
    public required IReadOnlyList<JobChange> Changes { get; init; }

    /// <summary>Gets the cursor to read from next.</summary>
    public required JobChangeCursor Cursor { get; init; }
}

/// <summary>
/// Where a change read resumes. Reads run in passes: a pass covers the change markers after
/// <see cref="PassAfter"/> through <see cref="PassThrough"/>, and pages forward through that range table by table.
/// </summary>
/// <remarks>
/// <para>
/// A transaction that stamped an earlier marker can commit after one that stamped a later marker, so a reader that
/// only moved forward would skip the first row. Each pass therefore starts an overlap window before the previous pass
/// ended, and reads the rows in that window again. Its upper bound is the store's latest marker when the pass starts,
/// and the pass pages forward until it reaches that bound before the next pass begins, so a burst of writes larger
/// than one page delays newer rows by some reads but never hides them.
/// </para>
/// <para>
/// The cursor round-trips through <see cref="ToString"/> and <see cref="TryParse"/>, so a reader on another node can
/// resume it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record JobChangeCursor
{
    private const string _FormatVersion = "v1";
    private const char _Separator = ':';
    private const string _NoPosition = "-";

    /// <summary>Gets the exclusive lower bound of the current pass.</summary>
    public required DateTime PassAfter { get; init; }

    /// <summary>Gets the inclusive upper bound of the current pass: the store's latest marker when it started.</summary>
    public required DateTime PassThrough { get; init; }

    /// <summary>Gets the last time job the current pass returned, or <see langword="null"/> before the first.</summary>
    public JobChangePosition? TimeJobs { get; init; }

    /// <summary>Gets the last cron job the current pass returned, or <see langword="null"/> before the first.</summary>
    public JobChangePosition? CronJobs { get; init; }

    /// <summary>
    /// Gets the last cron job occurrence the current pass returned, or <see langword="null"/> before the first.
    /// </summary>
    public JobChangePosition? CronJobOccurrences { get; init; }

    /// <summary>
    /// Returns a cursor at <paramref name="latestChangedAt"/>, so its first read returns the rows stamped after it and
    /// the rows its overlap window reads again.
    /// </summary>
    /// <param name="latestChangedAt">The store's latest change marker, or <see langword="null"/> for an empty store.</param>
    public static JobChangeCursor StartAt(DateTime? latestChangedAt)
    {
        var start = _Utc(latestChangedAt ?? DateTime.MinValue);

        return new JobChangeCursor { PassAfter = start, PassThrough = start };
    }

    /// <summary>Returns the position the current pass reached in the table of <paramref name="kind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public JobChangePosition? PositionOf(JobChangeKind kind)
    {
        return kind switch
        {
            JobChangeKind.TimeJob => TimeJobs,
            JobChangeKind.CronJob => CronJobs,
            JobChangeKind.CronJobOccurrence => CronJobOccurrences,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };
    }

    /// <summary>Returns this cursor with the position in the table of <paramref name="kind"/> moved.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public JobChangeCursor WithPosition(JobChangeKind kind, JobChangePosition position)
    {
        return kind switch
        {
            JobChangeKind.TimeJob => this with { TimeJobs = position },
            JobChangeKind.CronJob => this with { CronJobs = position },
            JobChangeKind.CronJobOccurrence => this with { CronJobOccurrences = position },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };
    }

    /// <summary>
    /// Returns the cursor of the pass after this one: it starts <paramref name="overlapWindow"/> before this pass's
    /// upper bound and runs through <paramref name="latestChangedAt"/>.
    /// </summary>
    /// <param name="latestChangedAt">The store's latest change marker now, or <see langword="null"/> for an empty store.</param>
    /// <param name="overlapWindow">How far back the next pass reads again.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="overlapWindow"/> is negative.</exception>
    public JobChangeCursor NextPass(DateTime? latestChangedAt, TimeSpan overlapWindow)
    {
        Argument.IsPositiveOrZero(overlapWindow);

        var after =
            PassThrough.Ticks - DateTime.MinValue.Ticks < overlapWindow.Ticks
                ? DateTime.MinValue
                : PassThrough - overlapWindow;
        var latest = latestChangedAt is { } value ? _Utc(value) : PassThrough;

        return new JobChangeCursor
        {
            PassAfter = _Utc(after),
            PassThrough = latest > PassThrough ? latest : PassThrough,
        };
    }

    /// <summary>Formats the cursor as a string <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        return string.Join(
            _Separator,
            _FormatVersion,
            PassAfter.Ticks.ToString(CultureInfo.InvariantCulture),
            PassThrough.Ticks.ToString(CultureInfo.InvariantCulture),
            _Format(TimeJobs),
            _Format(CronJobs),
            _Format(CronJobOccurrences)
        );
    }

    /// <summary>Reads a cursor <see cref="ToString"/> formatted.</summary>
    /// <param name="value">The formatted cursor.</param>
    /// <param name="cursor">The cursor, or <see langword="null"/> when <paramref name="value"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a cursor.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out JobChangeCursor? cursor)
    {
        cursor = null;
        var parts = value?.Split(_Separator);

        if (
            parts is not { Length: 6 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !_TryParseTicks(parts[1], out var after)
            || !_TryParseTicks(parts[2], out var through)
            || !_TryParsePosition(parts[3], out var timeJobs)
            || !_TryParsePosition(parts[4], out var cronJobs)
            || !_TryParsePosition(parts[5], out var occurrences)
        )
        {
            return false;
        }

        cursor = new JobChangeCursor
        {
            PassAfter = after,
            PassThrough = through,
            TimeJobs = timeJobs,
            CronJobs = cronJobs,
            CronJobOccurrences = occurrences,
        };

        return true;
    }

    private static string _Format(JobChangePosition? position)
    {
        return position is { } value
            ? value.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture) + "_" + value.Id.ToString("N")
            : _NoPosition;
    }

    private static bool _TryParsePosition(string value, out JobChangePosition? position)
    {
        position = null;

        if (string.Equals(value, _NoPosition, StringComparison.Ordinal))
        {
            return true;
        }

        var separator = value.IndexOf('_', StringComparison.Ordinal);

        if (
            separator < 0
            || !_TryParseTicks(value[..separator], out var changedAt)
            || !Guid.TryParseExact(value[(separator + 1)..], "N", out var id)
        )
        {
            return false;
        }

        position = new JobChangePosition(changedAt, id);

        return true;
    }

    private static bool _TryParseTicks(string value, out DateTime instant)
    {
        instant = default;

        if (
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
        )
        {
            return false;
        }

        instant = new DateTime(ticks, DateTimeKind.Utc);

        return true;
    }

    private static DateTime _Utc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
