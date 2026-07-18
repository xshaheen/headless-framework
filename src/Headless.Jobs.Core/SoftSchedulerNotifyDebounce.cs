// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

internal sealed class SoftSchedulerNotifyDebounce : IDisposable
{
    private readonly Action<string> _notifyCoreAction;
    private int _latest;
    private int _lastNotified = -1;
    private int _disposed;

    public SoftSchedulerNotifyDebounce(Action<string> notifyCoreAction)
    {
        _notifyCoreAction = notifyCoreAction;
    }

    /// <summary>
    /// Sends notifications in a thread-safe way and suppresses duplicates.
    /// Fires immediately for every change.
    /// </summary>
    internal void NotifySafely(int count)
    {
        Volatile.Write(ref _latest, count);

        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        // Call immediately so the reported thread count stays in sync
        _Callback();
    }

    /// <summary>
    /// Synchronously push the latest value now (used on shutdown).
    /// </summary>
    internal void Flush()
    {
        _Callback();
    }

    private void _Callback(object? _ = null)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        var latest = Volatile.Read(ref _latest);
        var last = Volatile.Read(ref _lastNotified);

        if (latest != 0 && latest == last)
        {
            return;
        }

        Volatile.Write(ref _lastNotified, latest);

        _notifyCoreAction?.Invoke(latest.ToString(CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
    }
}
