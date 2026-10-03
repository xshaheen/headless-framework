// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Sql;

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
