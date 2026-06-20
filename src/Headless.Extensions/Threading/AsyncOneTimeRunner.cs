// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.AsyncEx;

namespace Headless.Threading;

/// <summary>
/// This class is used to ensure running of a code block only once.
/// It can be instantiated as a static object to ensure that the code block runs only once in the application lifetime.
/// </summary>
[PublicAPI]
public sealed class AsyncOneTimeRunner : IDisposable
{
    private volatile bool _runBefore;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Runs <paramref name="action"/> once; subsequent successful calls are no-ops.</summary>
    /// <remarks>
    /// If <paramref name="action"/> throws, the runner is <b>not</b> marked as completed: the exception
    /// propagates to the caller and the next call retries. This makes the runner safe for one-time
    /// initialization that may fail transiently. The <paramref name="cancellationToken"/> bounds how long a
    /// caller waits to acquire the run lock; it does not cancel an in-flight <paramref name="action"/>.
    /// </remarks>
    public async Task RunAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        if (_runBefore)
        {
            return;
        }

        using (await _semaphore.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_runBefore)
            {
                return;
            }

            await action().ConfigureAwait(false);

            _runBefore = true;
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
