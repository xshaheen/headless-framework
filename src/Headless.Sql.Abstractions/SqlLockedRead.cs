// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

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
