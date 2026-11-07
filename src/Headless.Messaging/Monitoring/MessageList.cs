// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

/// <summary>
/// Which rows of one messaging table a keyset list, a capped count, or an Operation scope covers, narrowed by each
/// filter that is set. A filter left <see langword="null"/> matches every row. Text filters compare ordinally on every
/// store, trailing spaces included.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageListQuery
{
    /// <summary>Gets the table to read: published or received rows.</summary>
    public required MessageType MessageType { get; init; }

    /// <summary>Gets the one status to read, or <see langword="null"/> for every status.</summary>
    public StatusName? Status { get; init; }

    /// <summary>
    /// Gets a value indicating whether to read only terminal rows: <see cref="StatusName.Succeeded"/> or
    /// <see cref="StatusName.Failed"/> with no retry pending. With <see cref="Status"/> set to
    /// <see cref="StatusName.Failed"/> it reads the failures that ended for good.
    /// </summary>
    public bool TerminalOnly { get; init; }

    /// <summary>Gets the consumer identity every received row read must carry; ignored for published rows.</summary>
    public string? ConsumerIdentity { get; init; }

    /// <summary>Gets the message name every row read must carry.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the delivery lane every row read must carry.</summary>
    public MessageLane? Lane { get; init; }

    /// <summary>
    /// Gets the tenant every received inbox row read must carry; a received row without a tenant never matches it.
    /// Ignored for published rows, whose table records no tenant.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the application message id every row read must carry.</summary>
    public string? MessageId { get; init; }

    /// <summary>Gets the failure fingerprint every row read must carry.</summary>
    public string? FailureFingerprint { get; init; }

    /// <summary>
    /// Gets whether to read only held (<see langword="true"/>) or only unheld (<see langword="false"/>) received
    /// rows. Published rows are never held.
    /// </summary>
    public bool? IsHeld { get; init; }
}

/// <summary>The keyset position of one row in a message list: when it was added, then its storage identifier.</summary>
/// <remarks>
/// Neither value changes after a row is written, so a list paged on this position never repeats or skips a row that
/// changes state between pages. Stores order identifiers differently, so a position is meaningful only to the store
/// that produced it. The position round-trips through <see cref="ToString"/> and <see cref="TryParse"/>.
/// </remarks>
/// <param name="Added">The row's insert time.</param>
/// <param name="Id">The row's storage identifier.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct MessageListPosition(DateTimeOffset Added, Guid Id)
{
    private const string _FormatVersion = "l1";

    /// <summary>Formats the position as a string <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        return string.Join(
            ':',
            _FormatVersion,
            Added.UtcTicks.ToString(CultureInfo.InvariantCulture),
            Id.ToString("N")
        );
    }

    /// <summary>Reads a position <see cref="ToString"/> formatted.</summary>
    /// <param name="value">The formatted position.</param>
    /// <param name="position">The position, or the default value when <paramref name="value"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a position.</returns>
    public static bool TryParse(string? value, out MessageListPosition position)
    {
        position = default;
        var parts = value?.Split(':');

        if (
            parts is not { Length: 3 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[2], "N", out var id)
        )
        {
            return false;
        }

        position = new MessageListPosition(new DateTimeOffset(ticks, TimeSpan.Zero), id);

        return true;
    }
}

/// <summary>One page of a message list and the position of the next page.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageListPage
{
    /// <summary>Gets the rows, newest first. Their <see cref="MessageView.Content"/> is not projected.</summary>
    public required IReadOnlyList<MessageView> Items { get; init; }

    /// <summary>Gets the position to read the next page after, or <see langword="null"/> when this page is the last.</summary>
    public MessageListPosition? Next { get; init; }
}

/// <summary>A count that stops at a cap: a count above it reports the cap with <see cref="Capped"/> set.</summary>
/// <param name="Count">The count, at most the cap.</param>
/// <param name="Capped">Whether more rows matched than the cap.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct MessageCount(int Count, bool Capped)
{
    /// <summary>The cap a dashboard counts to: a larger count only says "more than this many".</summary>
    public const int DefaultCap = 10_000;

    /// <summary>Returns the count of <paramref name="matched"/> rows stopped at <paramref name="cap"/>.</summary>
    /// <param name="matched">How many rows the store counted, which may exceed the cap by the one row that proves it.</param>
    /// <param name="cap">The cap.</param>
    public static MessageCount Of(long matched, int cap)
    {
        return matched > cap ? new MessageCount(cap, Capped: true) : new MessageCount((int)matched, Capped: false);
    }
}

/// <summary>
/// The failures that ended for good with one cause: one failure fingerprint and, for received rows, one consumer.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageFailureGroup
{
    /// <summary>Gets the table the failures live in.</summary>
    public required MessageType MessageType { get; init; }

    /// <summary>
    /// Gets the failure fingerprint the failures share, or <see langword="null"/> for failures written without one.
    /// </summary>
    public string? FailureFingerprint { get; init; }

    /// <summary>Gets the consumer the received failures belong to, or <see langword="null"/> for published rows.</summary>
    public string? ConsumerIdentity { get; init; }

    /// <summary>Gets how many failures the group holds.</summary>
    public required int Count { get; init; }

    /// <summary>Gets the latest change marker among the failures: when the newest of them was written.</summary>
    public required DateTimeOffset LastFailedAt { get; init; }
}

/// <summary>The one order every store returns failure groups in, so a dashboard shows the same list on each.</summary>
internal static class MessageFailureGroupOrder
{
    /// <summary>Orders newest group first, then by fingerprint and consumer ordinally.</summary>
    public static IReadOnlyList<MessageFailureGroup> Sort(IEnumerable<MessageFailureGroup> groups)
    {
        return
        [
            .. groups
                .OrderByDescending(static group => group.LastFailedAt)
                .ThenBy(static group => group.FailureFingerprint, StringComparer.Ordinal)
                .ThenBy(static group => group.ConsumerIdentity, StringComparer.Ordinal),
        ];
    }
}

#pragma warning restore MA0048
