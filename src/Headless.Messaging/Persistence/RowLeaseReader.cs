// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Reads a row lease from the five lease columns a statement returns in <see cref="MessagingTables.LeaseColumns" />
/// order, so the in-memory copy carries the lease the store committed.
/// </summary>
internal static class RowLeaseReader
{
    /// <summary>The number of columns <see cref="ReadAsync" /> consumes.</summary>
    public const int ColumnCount = 5;

    /// <summary>Reads the lease whose generation is at <paramref name="ordinal" /> and whose other columns follow it.</summary>
    public static async ValueTask<RowLeaseSnapshot> ReadAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        var generation = Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        var state = (RowLeaseState)Convert.ToInt16(reader.GetValue(ordinal + 1), CultureInfo.InvariantCulture);
        DateTimeOffset? expiresAt = await reader.IsDBNullAsync(ordinal + 2, cancellationToken).ConfigureAwait(false)
            ? null
            : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal + 2, cancellationToken).ConfigureAwait(false);
        var holder = await reader.IsDBNullAsync(ordinal + 3, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(ordinal + 3);
        var takeovers = Convert.ToInt32(reader.GetValue(ordinal + 4), CultureInfo.InvariantCulture);

        return new RowLeaseSnapshot(generation, state, expiresAt, holder, takeovers);
    }
}
