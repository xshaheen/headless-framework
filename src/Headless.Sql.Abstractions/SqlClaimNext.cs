// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

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
