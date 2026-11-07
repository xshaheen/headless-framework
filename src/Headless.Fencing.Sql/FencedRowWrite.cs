// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;

namespace Headless.Fencing;

/// <summary>
/// What a consumer adds to a fenced row transition: its own assignments, an extra condition, and the columns to read
/// back, all inside the helper's statement, so they land only when the lease fence holds.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Set" /> and <see cref="Condition" /> are SQL fragments over the table's unqualified columns,
/// <see cref="SqlDialectTokens.Now" /> for the database clock, and parameters <see cref="Bind" /> adds under the
/// consumer's own names. Parameter names starting with <c>Lease</c>, and <c>now</c> and <c>applied</c>, are reserved for the
/// helper, and binding one throws.
/// The fragments are trusted SQL: never build them from input.
/// </para>
/// <para>
/// <see cref="Returning" /> names columns, unquoted; the helper quotes them, and they are read back as the table holds
/// them after the write.
/// </para>
/// </remarks>
/// <param name="Set">Assignments joined to the lease's own, such as <c>status = @status</c>.</param>
/// <param name="Condition">A predicate the row must also meet; a row that fails it is refused and nothing is written.</param>
/// <param name="Returning">Columns to read back from an accepted write, unquoted.</param>
/// <param name="Bind">Adds the parameters the fragments name.</param>
[PublicAPI]
public sealed record FencedRowWrite(
    string? Set = null,
    string? Condition = null,
    IReadOnlyList<string>? Returning = null,
    Action<DbCommand>? Bind = null
);

/// <summary>
/// A claim of the next rows a filter selects whose lease no live attempt holds: each claimed row is granted under its
/// own new generation.
/// </summary>
/// <remarks>
/// On PostgreSQL and SQL Server the claim skips rows another transaction has locked, so concurrent claimers never wait
/// on each other or hand one row to two. SQLite has one writer at a time, so claims there run one after another.
/// </remarks>
/// <param name="Filter">
/// A predicate over the table's unqualified columns and parameters <see cref="Bind" /> adds; the helper adds the lease's
/// own condition. Trusted SQL: never build it from input.
/// </param>
/// <param name="OrderBy">The order rows are claimed in: unqualified columns, each with an optional direction.</param>
/// <param name="BatchSize">The most rows to claim.</param>
/// <param name="Duration">How long each claimed row is held.</param>
/// <param name="Holder">The holder to record on each claimed row.</param>
/// <param name="Takeover">Whether an expired attempt can be claimed before a sweep abandons it.</param>
/// <param name="Bind">Adds the parameters <see cref="Filter" /> names; names starting with <c>Lease</c>, and <c>now</c> and <c>applied</c>, are reserved.</param>
[PublicAPI]
public sealed record FencedRowClaim(
    string Filter,
    IReadOnlyList<string> OrderBy,
    int BatchSize,
    TimeSpan Duration,
    string? Holder = null,
    LeaseTakeover Takeover = LeaseTakeover.Allowed,
    Action<DbCommand>? Bind = null
);

/// <summary>The lease a fenced row write found under its row lock, and how that lease judged the write's generation.</summary>
[PublicAPI]
public sealed class LockedRowLease
{
    internal LockedRowLease(RowLeaseSnapshot lease)
    {
        Lease = lease;
    }

    /// <summary>Gets the stored lease, read under the row lock before the write.</summary>
    public RowLeaseSnapshot Lease { get; }

    /// <summary>
    /// Gets how the lease judged the write's generation: <see cref="LeaseFenceStatus.Current" /> when the write applied,
    /// or, for a refused write, when the lease held but the write's own condition failed; otherwise why the fence
    /// refused it.
    /// </summary>
    /// <remarks>
    /// A refused write with a condition is classified by a second clock read under the same row lock, after the
    /// statement: a lease that expires between the two reports <see cref="LeaseFenceStatus.Expired" /> even when the
    /// condition was what refused the write. The error is always toward <c>Expired</c>, which is true by the time the
    /// caller sees it, never toward <see cref="LeaseFenceStatus.Current" />.
    /// </remarks>
    public LeaseFenceStatus Status { get; internal set; }
}
