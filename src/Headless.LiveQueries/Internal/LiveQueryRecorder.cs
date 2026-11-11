// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Collects what one run of a live query read. The runner begins it before it calls the query, so it flows down
/// into the query's own calls; nothing reads it back up the stack.
/// </summary>
internal sealed class LiveQueryRecorder
{
    private static readonly AsyncLocal<LiveQueryRecorder?> _Current = new();

    private readonly Lock _lock = new();

    // A null list means the whole key was read; otherwise a row matters when it passes any of the filters.
    private readonly Dictionary<string, List<Func<object, bool>>?> _reads = new(StringComparer.Ordinal);

    private volatile bool _ended;

    /// <summary>
    /// Gets the recorder of the live query this code runs in, or <see langword="null" /> outside one and once its
    /// run has returned.
    /// </summary>
    public static LiveQueryRecorder? Current => _Current.Value is { _ended: false } recorder ? recorder : null;

    /// <summary>
    /// Starts recording for one run. Call it synchronously inside the async method that awaits the query: the
    /// runtime then scopes the recorder to that method and the calls it makes, and restores the caller's state when
    /// the method returns.
    /// </summary>
    public static LiveQueryRecorder Begin()
    {
        var recorder = new LiveQueryRecorder();
        _Current.Value = recorder;

        return recorder;
    }

    /// <summary>
    /// Ends the run. Work that the query started and left running, such as a timer or a background loop, still
    /// carries this object as its ambient recorder for the rest of its life. It is no longer part of a live query:
    /// its writes are ordinary writes, and its reads have no run left to record them for.
    /// </summary>
    public void End()
    {
        _ended = true;
    }

    /// <summary>Records a read of every row of each key.</summary>
    public void Add(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            if (_ended)
            {
                return;
            }

            foreach (var key in keys)
            {
                _reads[key] = null;
            }
        }
    }

    /// <summary>Records a read of the rows of <paramref name="key" /> that pass <paramref name="filter" />.</summary>
    public void AddFiltered(string key, Func<object, bool> filter)
    {
        lock (_lock)
        {
            if (_ended)
            {
                return;
            }

            if (!_reads.TryGetValue(key, out var filters))
            {
                _reads[key] = [filter];
            }
            else
            {
                // An earlier unfiltered read of the key already covers every row.
                filters?.Add(filter);
            }
        }
    }

    /// <summary>Returns what was recorded so far.</summary>
    public LiveDependencies Snapshot()
    {
        lock (_lock)
        {
            return new LiveDependencies(
                _reads.ToDictionary(read => read.Key, read => read.Value?.ToArray(), StringComparer.Ordinal)
            );
        }
    }
}
