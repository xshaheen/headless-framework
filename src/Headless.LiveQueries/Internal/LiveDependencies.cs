// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>What one run of a live query depends on: whole keys, and keys narrowed by row filters.</summary>
/// <param name="reads">
/// The keys read. A <see langword="null" /> value means the whole key; otherwise a row of the key matters when it
/// passes any of the filters.
/// </param>
internal sealed class LiveDependencies(IReadOnlyDictionary<string, Func<object, bool>[]?> reads)
{
    public static readonly LiveDependencies None = new(
        new Dictionary<string, Func<object, bool>[]?>(StringComparer.Ordinal)
    );

    public bool IsEmpty => reads.Count == 0;

    public IEnumerable<string> Keys => reads.Keys;

    /// <summary>Returns whether <paramref name="writes" /> could change the result.</summary>
    /// <param name="writes">What a commit changed.</param>
    /// <param name="ruledOutRows">
    /// When the result is <see langword="false" />, how many changed rows belonged to a key this run read and passed
    /// none of its filters.
    /// </param>
    public bool Overlaps(LiveWriteSet writes, out int ruledOutRows)
    {
        ruledOutRows = 0;

        foreach (var key in writes.Keys)
        {
            if (reads.ContainsKey(key))
            {
                return true;
            }
        }

        foreach (var row in writes.Rows)
        {
            if (!reads.TryGetValue(row.Key, out var filters))
            {
                continue;
            }

            // Both sides: a row leaving a filter changes the result as much as one entering it.
            if (filters is null || _PassesAny(filters, row.Old) || _PassesAny(filters, row.New))
            {
                ruledOutRows = 0;

                return true;
            }

            ruledOutRows++;
        }

        return false;
    }

    private static bool _PassesAny(Func<object, bool>[] filters, object? row)
    {
        if (row is null)
        {
            return false;
        }

        foreach (var filter in filters)
        {
            if (filter(row))
            {
                return true;
            }
        }

        return false;
    }
}
