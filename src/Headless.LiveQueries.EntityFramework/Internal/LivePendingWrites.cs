// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>The tables and rows written inside one transaction that has not ended yet.</summary>
internal sealed class LivePendingWrites
{
    /// <summary>
    /// How many rows one commit describes one by one. Every subscription tests every row when the commit is
    /// announced, on the committing thread, so a bulk write past this is announced as its tables instead.
    /// </summary>
    public const int MaxRows = 256;

    private readonly Lock _lock = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly List<LiveRowChange> _rows = [];

    public void Add(LiveWriteSet writes)
    {
        lock (_lock)
        {
            _keys.UnionWith(writes.Keys);
            _rows.AddRange(writes.Rows);

            if (_rows.Count > MaxRows)
            {
                foreach (var row in _rows)
                {
                    _keys.Add(row.Key);
                }

                _rows.Clear();
            }
        }
    }

    /// <summary>Returns the writes and forgets them, so a second end of the same transaction announces nothing.</summary>
    public LiveWriteSet Take()
    {
        lock (_lock)
        {
            // A table written whole needs no row of its own.
            var writes = new LiveWriteSet([.. _keys], [.. _rows.Where(row => !_keys.Contains(row.Key))]);
            _keys.Clear();
            _rows.Clear();

            return writes;
        }
    }
}
