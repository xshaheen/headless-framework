// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>The tables written inside one transaction that has not ended yet.</summary>
internal sealed class LivePendingWrites
{
    private readonly Lock _lock = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public void Add(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            _keys.UnionWith(keys);
        }
    }

    /// <summary>Returns the tables and forgets them, so a second end of the same transaction announces nothing.</summary>
    public string[] Take()
    {
        lock (_lock)
        {
            string[] keys = [.. _keys];
            _keys.Clear();

            return keys;
        }
    }
}
