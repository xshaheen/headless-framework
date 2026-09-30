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
/// Claims the first row matching <see cref="Filter" /> in <see cref="OrderBy" /> order, skipping rows another
/// transaction has locked instead of waiting, and applies <see cref="Set" /> to it in the same statement. Its result
/// set holds <see cref="Returning" /> for the claimed row, or no row.
/// </summary>
/// <param name="Table">The quoted, qualified table.</param>
/// <param name="KeyColumns">The quoted columns that identify a row, to join the claim back to it.</param>
/// <param name="Filter">The claimable condition, over unqualified columns and <see cref="SqlDialectTokens.Now" />.</param>
/// <param name="OrderBy">The quoted visiting-order columns.</param>
/// <param name="Set">The assignments applied to the claimed row.</param>
/// <param name="Returning">Columns reported after the assignments.</param>
[PublicAPI]
public sealed record SqlClaimNext(
    string Table,
    IReadOnlyList<string> KeyColumns,
    string Filter,
    IReadOnlyList<string> OrderBy,
    string Set,
    IReadOnlyList<string> Returning
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
