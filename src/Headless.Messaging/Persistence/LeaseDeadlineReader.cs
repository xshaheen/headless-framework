// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Shared reader for the lease identity returned by a relational lease statement. The values are written by
/// the store in the ownership transition, so the in-memory model must mirror this durable generation.
/// </summary>
internal static class LeaseDeadlineReader
{
    /// <summary>Reads the returned lease identity; <see langword="null"/> when no row matched.</summary>
    public static async Task<(DateTimeOffset LockedUntil, string? Owner)?> ReadAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var lockedUntil = await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken).ConfigureAwait(false);
        var owner = await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(1);
        return (lockedUntil, owner);
    }
}
