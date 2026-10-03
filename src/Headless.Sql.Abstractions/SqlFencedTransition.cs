// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

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
