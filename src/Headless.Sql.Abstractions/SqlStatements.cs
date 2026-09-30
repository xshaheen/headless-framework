// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>One column of a row key, matched against a parameter.</summary>
/// <param name="Column">The quoted column.</param>
/// <param name="Parameter">The parameter name, without its prefix.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SqlKeyColumn(string Column, string Parameter);

/// <summary>
/// Reads one row by key and takes its update-intent lock until the transaction ends; when the row is absent, the key
/// is locked where the engine can (a key-range lock), so two first writers of one key serialize.
/// </summary>
/// <remarks>
/// Its result set holds the row's <see cref="Columns" />, or no row. The lock is update-intent rather than shared: a
/// reader that later writes in the same transaction would otherwise deadlock against a writer queued behind it.
/// </remarks>
[PublicAPI]
public sealed record SqlLockedRead(string Table, IReadOnlyList<SqlKeyColumn> Key, IReadOnlyList<string> Columns);

/// <summary>
/// The fenced state transition: one conditional <c>UPDATE</c> of the keyed row whose <c>WHERE</c> carries the fence,
/// so the check and the write are one decision, or, without <see cref="Set" />, the same check alone.
/// </summary>
/// <remarks>
/// Run it in the batch after a <see cref="SqlLockedRead" /> of the same key: the locked read waits out any other
/// holder and reports the row as it was, which is what a rejection is classified from; this statement then reads the
/// clock and decides. Its result set is one row: whether the transition applied, then <see cref="Returning" />
/// (all null when it did not).
/// </remarks>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="Key">The row's key.</param>
/// <param name="Fence">
/// The condition the row must meet for the transition to apply, over unqualified columns and
/// <see cref="SqlDialectTokens.Now" />.
/// </param>
/// <param name="Set">
/// The assignments, over unqualified columns (each reads the row as it was) and <see cref="SqlDialectTokens.Now" />,
/// or <see langword="null" /> to evaluate the fence without writing.
/// </param>
/// <param name="Returning">Columns reported as written when the transition applied.</param>
[PublicAPI]
public sealed record SqlFencedTransition(
    string Table,
    IReadOnlyList<SqlKeyColumn> Key,
    string Fence,
    string? Set,
    IReadOnlyList<string> Returning
);

/// <summary>
/// Inserts the keyed row only when no row has the key, without raising a duplicate-key error. Its result set is one
/// row: whether the row was inserted, then <see cref="Returning" />.
/// </summary>
/// <remarks>
/// On an engine that cannot lock an absent key, a concurrent insert of the same key waits for the other transaction
/// and then inserts nothing; the caller's next locked read finds the committed row.
/// </remarks>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="Key">The row's key; its parameters supply the key columns' values.</param>
/// <param name="Columns">The non-key columns written.</param>
/// <param name="Values">One expression per column in <see cref="Columns" />, over <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="Returning">Columns reported as written.</param>
[PublicAPI]
public sealed record SqlInsertIfAbsent(
    string Table,
    IReadOnlyList<SqlKeyColumn> Key,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Returning
);

/// <summary>
/// Claims the first row, or the first <c>@BatchSizeParameter</c> rows, matching <see cref="Filter" /> in
/// <see cref="OrderBy" /> order, skipping rows another transaction has locked instead of waiting, and applies
/// <see cref="Set" /> to them in the same statement. Its result set holds <see cref="Returning" /> for each claimed
/// row, in no particular order, or no row.
/// </summary>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="KeyColumns">The quoted columns that identify a row, to join the claim back to it.</param>
/// <param name="Filter">The claimable condition, over unqualified columns and <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="OrderBy">The quoted visiting-order columns.</param>
/// <param name="Set">The assignments applied to the claimed row.</param>
/// <param name="Returning">Columns reported after the assignments.</param>
/// <param name="BatchSizeParameter">
/// The parameter that bounds how many rows one statement claims, or <see langword="null" /> to claim one.
/// </param>
[PublicAPI]
public sealed record SqlClaimNext(
    string Table,
    IReadOnlyList<string> KeyColumns,
    string Filter,
    IReadOnlyList<string> OrderBy,
    string Set,
    IReadOnlyList<string> Returning,
    string? BatchSizeParameter = null
);

/// <summary>
/// Deletes at most <c>@BatchSizeParameter</c> rows matching <see cref="Filter" />, skipping locked rows, so a large
/// purge is many short transactions that never wait on a live writer. Reports the deleted count.
/// </summary>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="KeyColumns">The quoted columns that identify a row.</param>
/// <param name="Filter">The deletable condition, over unqualified columns and <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="BatchSizeParameter">The parameter that bounds the batch.</param>
[PublicAPI]
public sealed record SqlDeleteBatch(
    string Table,
    IReadOnlyList<string> KeyColumns,
    string Filter,
    string BatchSizeParameter
);

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

/// <summary>
/// One statement that reads <see cref="SqlDialectTokens.Now" /> without locking anything first: a plain read, a
/// delete, or an update whose clock may be taken when the statement starts.
/// </summary>
/// <remarks>
/// Use it for statements that do not wait on a row another writer holds before they decide. A statement that must
/// read the clock after a lock wait belongs in a <see cref="SqlFencedTransition" />.
/// </remarks>
/// <param name="Sql">
/// Exactly one statement, which does not itself start with <c>WITH</c>, over <see cref="SqlDialectTokens.Now" />.
/// </param>
[PublicAPI]
public sealed record SqlClockedStatement(string Sql);
