// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Shared reader for the lease deadline a lease statement returns (PostgreSQL <c>RETURNING "LockedUntil"</c>,
/// SQL Server <c>OUTPUT inserted.LockedUntil</c>). The deadline is written by the store's own clock, so the
/// value read back is the durable one the in-memory model must mirror.
/// </summary>
internal static class LeaseDeadlineReader
{
    /// <summary>Reads the returned lease deadline; <see langword="null"/> when no row matched.</summary>
    public static async Task<DateTime?> ReadAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
    }
}
