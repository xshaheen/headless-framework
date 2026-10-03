// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

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
