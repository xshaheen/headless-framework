// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;

namespace Tests;

/// <summary>Builds the lease a seeded job row carries.</summary>
internal static class TestLeases
{
    /// <summary>
    /// A row seeded as held by <paramref name="holder"/> until <paramref name="expiresAt"/>: its first grant, still
    /// active. A deadline in the past seeds a lapsed lease. With neither a holder nor a deadline the row was never
    /// granted, so this returns <see langword="default"/>, which lets a seed that decides ownership per case pass
    /// both through one call.
    /// </summary>
    public static RowLeaseSnapshot Held(string? holder, DateTimeOffset? expiresAt, long generation = 1) =>
        holder is null && expiresAt is null
            ? default
            : new RowLeaseSnapshot(generation, RowLeaseState.Active, expiresAt, holder, TakeoverCount: 0);

    /// <inheritdoc cref="Held(string?, DateTimeOffset?, long)"/>
    /// <remarks>
    /// Many seeds keep their instants as <see cref="DateTime"/>. Converting one to an offset reads its
    /// <see cref="DateTime.Kind"/>, and an unspecified kind would silently shift the deadline by the machine's
    /// offset, so a non-UTC value fails the seed instead.
    /// </remarks>
    public static RowLeaseSnapshot Held(string? holder, DateTime expiresAt, long generation = 1)
    {
        if (expiresAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A seeded lease deadline must be UTC.", nameof(expiresAt));
        }

        return Held(holder, new DateTimeOffset(expiresAt), generation);
    }
}
