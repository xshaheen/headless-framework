// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>
/// Inserts the keyed row, or, when it exists and <see cref="Guard" /> allows, updates it, as one statement that never
/// raises a duplicate-key error. Its result set is one row: the <see cref="SqlUpsertOutcome" /> as a small integer,
/// then <see cref="Returning" /> as written (all null when the guard refused).
/// </summary>
/// <remarks>
/// <see cref="SqlDialectTokens.Now" /> is the statement's own clock. On SQL Server it is read after the key lock is
/// taken; on PostgreSQL it is read when the statement starts, before it waits on a concurrent writer of the same key.
/// A value that must never move backwards (a heartbeat) therefore takes the later of the stored value and the clock in
/// <see cref="Set" />.
/// </remarks>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="Key">The row's key; it is also the conflict target, so it must be the primary key or a unique index.</param>
/// <param name="Columns">The quoted non-key columns an insert writes.</param>
/// <param name="Values">The values of <see cref="Columns" />, over parameters and <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="Set">
/// The assignments an update applies: unqualified target columns, each set from the stored row's columns written as
/// <see cref="SqlDialectTokens.Stored" /><c>.column</c>, parameters, and <see cref="SqlDialectTokens.Now" />. It never
/// reads the proposed insert values: pass them again as parameters.
/// </param>
/// <param name="Guard">
/// The condition the stored row must meet to be updated, over the same terms as <see cref="Set" />'s right-hand sides,
/// or <see langword="null" /> to always update.
/// </param>
/// <param name="Returning">Columns reported as written.</param>
[PublicAPI]
public sealed record SqlUpsert(
    string Table,
    IReadOnlyList<SqlKeyColumn> Key,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Values,
    string Set,
    string? Guard,
    IReadOnlyList<string> Returning
);

/// <summary>What a <see cref="SqlUpsert" /> did.</summary>
[PublicAPI]
public enum SqlUpsertOutcome
{
    /// <summary>The row existed and the guard refused the update; nothing was written.</summary>
    Refused = 0,

    /// <summary>No row had the key; it was inserted.</summary>
    Inserted = 1,

    /// <summary>The row existed and was updated.</summary>
    Updated = 2,
}
