// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Helpers;

/// <summary>
/// Items a telemetry processor captures as they end. A test awaits the item it needs instead of polling for it: each
/// <see cref="Add"/> completes the waiters it satisfies.
/// </summary>
internal sealed class CapturedItems<T>
{
    private readonly Lock _sync = new();
    private readonly List<T> _items = [];
    private readonly List<(Func<T, bool> Matches, TaskCompletionSource<T> Source)> _waiters = [];

    public void Add(T item)
    {
        lock (_sync)
        {
            _items.Add(item);

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Matches(item))
                {
                    _waiters[i].Source.TrySetResult(item);
                    _waiters.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>The number of captured items that match, now.</summary>
    public int Count(Func<T, bool> matches)
    {
        lock (_sync)
        {
            return _items.Count(matches);
        }
    }

    /// <summary>Returns the first captured item that matches, waiting for one to arrive when none has yet.</summary>
    public Task<T> WaitForAsync(Func<T, bool> matches)
    {
        lock (_sync)
        {
            foreach (var item in _items)
            {
                if (matches(item))
                {
                    return Task.FromResult(item);
                }
            }

            var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((matches, source));

            return source.Task.Bounded();
        }
    }
}
