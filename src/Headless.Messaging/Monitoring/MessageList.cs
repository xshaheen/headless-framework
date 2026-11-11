// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Headless.Checks;

namespace Headless.Messaging;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

/// <summary>The value a message list sorts on; rows with one value list in storage-identifier order.</summary>
[PublicAPI]
public enum MessageListSortKey
{
    /// <summary>When the row was added.</summary>
    Added = 0,

    /// <summary>The message name, compared in the store's order: its column collation, ordinal in memory.</summary>
    Name = 1,
}

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

    /// <summary>Gets the inclusive lower bound of the time a row was added.</summary>
    public DateTimeOffset? AddedFrom { get; init; }

    /// <summary>Gets the exclusive upper bound of the time a row was added.</summary>
    public DateTimeOffset? AddedBefore { get; init; }

    /// <summary>
    /// Gets the text every row read must contain in its stored content, the serialized envelope of headers and body,
    /// as a literal substring compared without regard to case. <see langword="null"/> or empty matches every row.
    /// </summary>
    /// <remarks>
    /// No index serves the match, so it reads every row the other filters leave. Bound the read with a cancellation
    /// token, and keep the text out of URLs and logs: it is whatever an operator searched a message body for.
    /// </remarks>
    public string? Content { get; init; }

    /// <summary>
    /// Gets the correlation id (<see cref="Headers.CorrelationId"/>) every row read must carry, compared exactly on
    /// the value without regard to case. <see langword="null"/> or empty matches every row.
    /// </summary>
    /// <remarks>
    /// No column stores the correlation id, so a relational store finds it in the stored envelope: it matches the
    /// header written as the configured <see cref="MessagingOptions.JsonSerializerOptions"/> write it, which no body
    /// text can imitate unless the body holds an object with a property of the header's exact name. Like
    /// <see cref="Content"/>, the match reads every row the other filters leave, and a store whose envelope a
    /// replaced <see cref="IMessageSerializer"/> writes in another format finds nothing.
    /// </remarks>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the value the list sorts on, then the storage identifier. The default is the added time.</summary>
    public MessageListSortKey SortBy { get; init; }

    /// <summary>
    /// Gets the direction of the list's order, applied to the sort key and the storage identifier alike. The default
    /// is descending: newest first. A position is meaningful only under the sort and direction it was read with.
    /// </summary>
    public ListSortDirection SortDirection { get; init; } = ListSortDirection.Descending;

    /// <summary>Gets a value indicating whether the list reads in descending order.</summary>
    public bool Descending => SortDirection == ListSortDirection.Descending;
}

/// <summary>
/// The keyset position of one row in a message list: when it was added, its name, then its storage identifier. A read
/// uses the value of its query's <see cref="MessageListQuery.SortBy"/> and the identifier, and ignores the other.
/// </summary>
/// <remarks>
/// Neither value changes after a row is written, so a list paged on this position never repeats or skips a row that
/// changes state between pages. Stores order identifiers and names differently, so a position is meaningful only to
/// the store that produced it, under the sort and direction it was read with. The position round-trips through
/// <see cref="ToString"/> and <see cref="TryParse"/>, which never writes the separators <c>:</c>, <c>|</c> or
/// <c>=</c> inside a value.
/// </remarks>
/// <param name="Added">The row's insert time.</param>
/// <param name="Name">The row's message name.</param>
/// <param name="Id">The row's storage identifier.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct MessageListPosition(DateTimeOffset Added, string Name, Guid Id)
{
    private const string _FormatVersion = "l2";

    /// <summary>Returns the position of <paramref name="view"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    public static MessageListPosition Of(MessageView view)
    {
        Argument.IsNotNull(view);

        return new(view.Added, view.Name, view.StorageId);
    }

    /// <summary>Formats the position as a string <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        return string.Join(
            ':',
            _FormatVersion,
            Added.UtcTicks.ToString(CultureInfo.InvariantCulture),
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Name ?? "")),
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
            parts is not { Length: 4 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !_TryDecode(parts[2], out var name)
            || !Guid.TryParseExact(parts[3], "N", out var id)
        )
        {
            return false;
        }

        position = new MessageListPosition(new DateTimeOffset(ticks, TimeSpan.Zero), name, id);

        return true;
    }

    private static bool _TryDecode(string value, [NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!Base64Url.IsValid(value))
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(
                Base64Url.DecodeFromChars(value)
            );

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

/// <summary>One page of a message list and the position of the next page.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record MessageListPage
{
    /// <summary>The most rows one list page returns.</summary>
    public const int MaxPageSize = 500;

    /// <summary>
    /// Gets the rows, in the query's order. Their <see cref="MessageView.Content"/> is not projected.
    /// </summary>
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
