// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>What one commit changed: whole keys, and rows of a key whose values are known.</summary>
/// <param name="Keys">Keys changed in a way no row describes; every query that depends on one is re-run.</param>
/// <param name="Rows">Changed rows; a query with row filters on the row's key is re-run only when a filter passes.</param>
internal sealed record LiveWriteSet(IReadOnlyCollection<string> Keys, IReadOnlyList<LiveRowChange> Rows)
{
    public bool IsEmpty => Keys.Count == 0 && Rows.Count == 0;
}
