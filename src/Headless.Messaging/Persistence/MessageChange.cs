// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Messaging.Persistence;

/// <summary>One published or received message row whose change marker a change read returned.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageChange
{
    /// <summary>Gets the table the row lives in.</summary>
    public required MessageChangeKind Kind { get; init; }

    /// <summary>Gets the row's storage identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the row's change marker as the read saw it: the store's clock when the write that last changed the row's
    /// visible state ran, moved past the row's previous marker when the clock had not passed it.
    /// </summary>
    public required DateTimeOffset ChangedAt { get; init; }

    /// <summary>Gets the row's status as the read saw it.</summary>
    public required StatusName Status { get; init; }

    /// <summary>
    /// Gets the row's version: its change marker in UTC ticks. Every state change raises it, so a reader that already
    /// handled a version of a row can drop a repeat.
    /// </summary>
    public long Version => ChangedAt.UtcTicks;
}

/// <summary>The messaging table a changed row lives in.</summary>
[PublicAPI]
#pragma warning disable CA1008 // Every changed row lives in a table, so a zero member would name none.
public enum MessageChangeKind
{
    /// <summary>A published (outbox) message.</summary>
    Published = 1,

    /// <summary>A received (inbox) message.</summary>
    Received = 2,
}
#pragma warning restore CA1008

/// <summary>The keyset position of one row in a change read: its change marker, then its identifier.</summary>
/// <remarks>
/// The identifier breaks ties between rows one statement stamped with the same marker. Stores order identifiers
/// differently, so a position is meaningful only to the store that produced it.
/// </remarks>
/// <param name="ChangedAt">The row's change marker.</param>
/// <param name="Id">The row's storage identifier.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct MessageChangePosition(DateTimeOffset ChangedAt, Guid Id);

/// <summary>One bounded change read: the changed rows and the cursor to read from next.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageChangePage
{
    /// <summary>Gets the changed rows, in change-marker order within each table.</summary>
    public required IReadOnlyList<MessageChange> Changes { get; init; }

    /// <summary>Gets the cursor to read from next.</summary>
    public required MessageChangeCursor Cursor { get; init; }
}

/// <summary>
/// Where a change read resumes. Reads run in passes: a pass covers the change markers after
/// <see cref="PassAfter"/> through <see cref="PassThrough"/>, and pages forward through that range table by table.
/// </summary>
/// <remarks>
/// <para>
/// A marker is stamped when its statement runs, not when its transaction commits, so a transaction that stamped an
/// earlier marker can commit after one that stamped a later marker, and a reader that only moved forward would skip
/// the first row. Each pass therefore starts an overlap window before the previous pass ended, and reads the rows in
/// that window again. Its upper bound is the store's latest marker when the pass starts, and the pass pages forward
/// until it reaches that bound before the next pass begins, so a burst of writes larger than one page delays newer
/// rows by some reads but never hides them.
/// </para>
/// <para>
/// The cursor round-trips through <see cref="ToString"/> and <see cref="TryParse"/>, so a reader on another node can
/// resume it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record MessageChangeCursor
{
    private const string _FormatVersion = "v1";
    private const char _Separator = ':';
    private const string _NoPosition = "-";

    /// <summary>
    /// Gets the overlap window a reader uses unless it is configured otherwise. It must exceed the longest time between
    /// stamping a row and committing it, which for an outbox write enlisted in an application transaction is the
    /// longest such transaction.
    /// </summary>
    public static TimeSpan DefaultOverlapWindow { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets the exclusive lower bound of the current pass.</summary>
    public required DateTimeOffset PassAfter { get; init; }

    /// <summary>Gets the inclusive upper bound of the current pass: the store's latest marker when it started.</summary>
    public required DateTimeOffset PassThrough { get; init; }

    /// <summary>Gets the last published row the current pass returned, or <see langword="null"/> before the first.</summary>
    public MessageChangePosition? Published { get; init; }

    /// <summary>Gets the last received row the current pass returned, or <see langword="null"/> before the first.</summary>
    public MessageChangePosition? Received { get; init; }

    /// <summary>
    /// Returns a cursor at <paramref name="latestChangedAt"/>, so its first read returns the rows stamped after it and
    /// the rows its overlap window reads again.
    /// </summary>
    /// <param name="latestChangedAt">The store's latest change marker, or <see langword="null"/> for an empty store.</param>
    public static MessageChangeCursor StartAt(DateTimeOffset? latestChangedAt)
    {
        var start = (latestChangedAt ?? DateTimeOffset.MinValue).ToUniversalTime();

        return new MessageChangeCursor { PassAfter = start, PassThrough = start };
    }

    /// <summary>Returns the position the current pass reached in the table of <paramref name="kind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public MessageChangePosition? PositionOf(MessageChangeKind kind)
    {
        return kind switch
        {
            MessageChangeKind.Published => Published,
            MessageChangeKind.Received => Received,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };
    }

    /// <summary>Returns this cursor with the position in the table of <paramref name="kind"/> moved.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public MessageChangeCursor WithPosition(MessageChangeKind kind, MessageChangePosition position)
    {
        return kind switch
        {
            MessageChangeKind.Published => this with { Published = position },
            MessageChangeKind.Received => this with { Received = position },
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
    public MessageChangeCursor NextPass(DateTimeOffset? latestChangedAt, TimeSpan overlapWindow)
    {
        Argument.IsPositiveOrZero(overlapWindow);

        var after =
            PassThrough.UtcTicks - DateTimeOffset.MinValue.UtcTicks < overlapWindow.Ticks
                ? DateTimeOffset.MinValue
                : PassThrough - overlapWindow;
        var latest = latestChangedAt is { } value ? value.ToUniversalTime() : PassThrough;

        return new MessageChangeCursor
        {
            PassAfter = after.ToUniversalTime(),
            PassThrough = latest > PassThrough ? latest : PassThrough,
        };
    }

    /// <summary>Formats the cursor as a string <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        return string.Join(
            _Separator,
            _FormatVersion,
            PassAfter.UtcTicks.ToString(CultureInfo.InvariantCulture),
            PassThrough.UtcTicks.ToString(CultureInfo.InvariantCulture),
            _Format(Published),
            _Format(Received)
        );
    }

    /// <summary>Reads a cursor <see cref="ToString"/> formatted.</summary>
    /// <param name="value">The formatted cursor.</param>
    /// <param name="cursor">The cursor, or <see langword="null"/> when <paramref name="value"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a cursor.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out MessageChangeCursor? cursor)
    {
        cursor = null;
        var parts = value?.Split(_Separator);

        if (
            parts is not { Length: 5 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !_TryParseTicks(parts[1], out var after)
            || !_TryParseTicks(parts[2], out var through)
            || !_TryParsePosition(parts[3], out var published)
            || !_TryParsePosition(parts[4], out var received)
        )
        {
            return false;
        }

        cursor = new MessageChangeCursor
        {
            PassAfter = after,
            PassThrough = through,
            Published = published,
            Received = received,
        };

        return true;
    }

    private static string _Format(MessageChangePosition? position)
    {
        return position is { } value
            ? value.ChangedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + "_" + value.Id.ToString("N")
            : _NoPosition;
    }

    private static bool _TryParsePosition(string value, out MessageChangePosition? position)
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

        position = new MessageChangePosition(changedAt, id);

        return true;
    }

    private static bool _TryParseTicks(string value, out DateTimeOffset instant)
    {
        instant = default;

        if (
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
        )
        {
            return false;
        }

        instant = new DateTimeOffset(ticks, TimeSpan.Zero);

        return true;
    }
}
