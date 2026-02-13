using System.Runtime.CompilerServices;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

public sealed class ScheduledMediumMessageQueue(TimeProvider timeProvider) : IDisposable
{
    private readonly SortedSet<(long, MediumMessage)> _queue = new(
        Comparer<(long, MediumMessage)>.Create(
            (a, b) =>
            {
                var result = a.Item1.CompareTo(b.Item1);
                return result == 0 ? string.CompareOrdinal(a.Item2.DbId, b.Item2.DbId) : result;
            }
        )
    );

    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly Lock _lock = new();
    private bool _isDisposed;

    public void Enqueue(MediumMessage message, long sendTime)
    {
        lock (_lock)
        {
            _queue.Add((sendTime, message));
        }

        _semaphore.Release();
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public IEnumerable<MediumMessage> UnorderedItems
    {
        get
        {
            lock (_lock)
            {
                return _queue.Select(x => x.Item2).ToList();
            }
        }
    }

    public async IAsyncEnumerable<MediumMessage> GetConsumingEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _semaphore.WaitAsync(cancellationToken);

            (long, MediumMessage)? nextItem = null;

            lock (_lock)
            {
                if (_queue.Count > 0)
                {
                    var topMessage = _queue.First();
                    var timeLeft = topMessage.Item1 - timeProvider.GetUtcNow().UtcDateTime.Ticks;
                    if (timeLeft < 500000) // 50ms
                    {
                        nextItem = topMessage;
                        _queue.Remove(topMessage);
                    }
                }
            }

            if (nextItem is not null)
            {
                yield return nextItem.Value.Item2;
            }
            else
            {
                // Re-release the semaphore if no item is ready yet
                _semaphore.Release();
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        _Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void _Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _semaphore.Dispose();
        }

        _isDisposed = true;
    }

#pragma warning disable MA0055 // Dispose methods should call SuppressFinalize
    ~ScheduledMediumMessageQueue()
#pragma warning restore MA0055
    {
        _Dispose(disposing: false);
    }
}
