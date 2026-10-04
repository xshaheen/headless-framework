// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Helpers for detached (fire-and-forget) tasks shared by the cache coordinator and the Hybrid tier.
/// </summary>
internal static class CacheDetachedTask
{
    /// <summary>
    /// Disposes a transferred internal <see cref="CancellationTokenSource"/> only after the detached
    /// <paramref name="task"/> completes, so a still-running, non-cooperative operation never touches a disposed
    /// token source (for example via <see cref="CancellationToken.WaitHandle"/>). Cancellation must already have
    /// signalled the operation; disposal only frees the timer, which can wait until the token is released.
    /// </summary>
    /// <remarks>
    /// Always schedules on <see cref="TaskScheduler.Default"/> (never <c>ExecuteSynchronously</c>) so disposal
    /// cannot run inline on an I/O completion thread of the operation that just finished.
    /// </remarks>
    public static void DisposeAfter(CancellationTokenSource cts, Task task)
    {
        _ = task.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            cts,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
    }
}
