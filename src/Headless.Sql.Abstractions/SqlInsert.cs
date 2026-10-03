// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

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
