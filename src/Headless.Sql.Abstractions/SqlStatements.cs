// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
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
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="Key">The row's key.</param>
/// <param name="Columns">The columns read.</param>
/// <param name="KeyPredicate">
/// The filter of a partial unique index the key belongs to, over unqualified columns and literals, or
/// <see langword="null" /> when the key is the primary key or a full unique index. It is matched as written, so the
/// engine can use that index.
/// </param>
[PublicAPI]
public sealed record SqlLockedRead(
    string Table,
    IReadOnlyList<SqlKeyColumn> Key,
    IReadOnlyList<string> Columns,
    string? KeyPredicate = null
);

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
/// <param name="KeyPredicate">
/// The filter of the partial unique index that is the conflict target, over unqualified columns and literals, or
/// <see langword="null" /> when the key is the primary key or a full unique index. Only rows it matches count as
/// holding the key.
/// </param>
[PublicAPI]
public sealed record SqlInsertIfAbsent(
    string Table,
    IReadOnlyList<SqlKeyColumn> Key,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Returning,
    string? KeyPredicate = null
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

/// <summary>
/// Inserts one row and reports the values it wrote, such as the database clock it stamped. Its result set is one row
/// holding <see cref="Returning" />.
/// </summary>
/// <remarks>
/// A plain insert takes no lock on any other row, so it reads <see cref="SqlDialectTokens.Now" /> when it starts. It
/// raises the engine's duplicate-key error on a key that exists; use <see cref="SqlInsertIfAbsent" /> where one may.
/// </remarks>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="Columns">The quoted columns written.</param>
/// <param name="Values">One expression per column, over parameters and <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="Returning">At least one quoted column, reported as written.</param>
[PublicAPI]
public sealed record SqlInsert(
    string Table,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Returning
);

/// <summary>
/// Reads the first <c>@BatchSizeParameter</c> rows matching <see cref="Filter" /> in <see cref="OrderBy" /> order and
/// takes their update-intent locks until the transaction ends, skipping rows another transaction has locked instead of
/// waiting. Its result set holds <see cref="Columns" /> for each locked row, in <see cref="OrderBy" /> order.
/// </summary>
/// <remarks>
/// Use it when the rows are written later in the same transaction, or must stay with this transaction while it works
/// on them, rather than being updated by the same statement as <see cref="SqlClaimNext" /> does. Nothing waits, so the
/// statement reads <see cref="SqlDialectTokens.Now" /> when it starts.
/// </remarks>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="Columns">
/// The quoted columns, or expressions over unqualified columns and <see cref="SqlDialectTokens.Now" />, read from each
/// row.
/// </param>
/// <param name="Filter">The condition, over unqualified columns and <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="OrderBy">The quoted visiting-order columns, each optionally followed by <c>ASC</c> or <c>DESC</c>.</param>
/// <param name="BatchSizeParameter">The parameter that bounds how many rows are locked.</param>
[PublicAPI]
public sealed record SqlLockBatch(
    string Table,
    IReadOnlyList<string> Columns,
    string Filter,
    IReadOnlyList<string> OrderBy,
    string BatchSizeParameter
);

/// <summary>
/// Takes an exclusive lock on an application-defined resource name until the transaction ends, waiting while another
/// transaction holds it, so callers that share a name serialize even when no row exists to lock yet.
/// </summary>
/// <remarks>
/// The wait is bounded by the command timeout. The name is hashed on PostgreSQL (an advisory lock), and used as is on
/// SQL Server (an application lock), where it may hold at most 255 characters.
/// </remarks>
/// <param name="ResourceParameter">The text parameter that holds the resource name.</param>
[PublicAPI]
public sealed record SqlTransactionLock(string ResourceParameter);
