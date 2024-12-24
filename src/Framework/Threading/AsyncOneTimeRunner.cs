// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Nito.AsyncEx;

namespace Framework.Threading;

/// <summary>
/// This class is used to ensure running of a code block only once.
/// It can be instantiated as a static object to ensure that the code block runs only once in the application lifetime.
/// </summary>
[PublicAPI]
public sealed class AsyncOneTimeRunner : IDisposable
{
    private volatile bool _runBefore;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task RunAsync(Func<Task> action)
    {
        if (_runBefore)
        {
            return;
        }

        using (await _semaphore.LockAsync())
        {
            if (_runBefore)
            {
                return;
            }

            await action();

            _runBefore = true;
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
