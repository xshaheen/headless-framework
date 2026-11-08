// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Where the operator ledger resumes: the time and operation identifier of the last receipt a page returned. Neither
/// changes after a receipt is written, so paging never repeats or skips a receipt.
/// </summary>
/// <remarks>The text form is <c>r1:{UTC ticks}:{operation id}</c>; the client treats it as opaque.</remarks>
internal static class ReceiptListCursor
{
    private const string _FormatVersion = "r1";

    public static string Format(MessagingOperationReceiptPosition position) =>
        string.Join(
            ':',
            _FormatVersion,
            position.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            position.OperationId.ToString("N")
        );

    /// <summary>Reads a cursor; an empty value is the first page, read as a <see langword="null"/> position.</summary>
    public static bool TryParse(string? value, out MessagingOperationReceiptPosition? position)
    {
        position = null;

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var parts = value.Split(':');

        if (
            parts is not { Length: 3 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[2], "N", out var operationId)
        )
        {
            return false;
        }

        position = new MessagingOperationReceiptPosition(new DateTimeOffset(ticks, TimeSpan.Zero), operationId);

        return true;
    }
}
