// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

internal sealed class ScheduledMediumMessageQueue(TimeProvider timeProvider, int capacity = 1000) : IDisposable
{
    private readonly int _capacity = Argument.IsPositive(capacity);
    private readonly SortedSet<(long, MediumMessage)> _queue = new(
        Comparer<(long, MediumMessage)>.Create(
            (a, b) =>
            {
                var result = a.Item1.CompareTo(b.Item1);
                return result == 0 ? a.Item2.StorageId.CompareTo(b.Item2.StorageId) : result;
            }
        )
    );

    private readonly SemaphoreSlim _changedSignal = new(0);
    private readonly Lock _lock = new();
    private bool _isDisposed;

    public bool TryEnqueue(MediumMessage message, long sendTime)
    {
        var shouldSignal = false;

        lock (_lock)
        {
            if (_queue.Contains((sendTime, message)))
            {
                return true;
            }

            if (_queue.Count >= _capacity)
            {
                return false;
            }

            var previousEarliest = _queue.Count > 0 ? _queue.Min.Item1 : (long?)null;

            if (!_queue.Add((sendTime, message)))
            {
                return false;
            }

            shouldSignal = previousEarliest is null || sendTime < previousEarliest.Value;
        }

        if (shouldSignal)
        {
            _changedSignal.Release();
        }

        return true;
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
            var nextItem = _TryDequeueDueMessage(out var delay);

            if (nextItem is not null)
            {
                yield return nextItem;
            }
            else if (delay is not null)
            {
                await _WaitUntilDueOrChangedAsync(delay.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _changedSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            _changedSignal.Dispose();
        }

        _isDisposed = true;
    }

#pragma warning disable MA0055 // Dispose methods should call SuppressFinalize
    ~ScheduledMediumMessageQueue()
#pragma warning restore MA0055
    {
        if (!_isDisposed)
        {
            System.Diagnostics.Debug.Fail(
                "ScheduledMediumMessageQueue was not disposed. Call Dispose() to release SemaphoreSlim."
            );
        }
    }

    private MediumMessage? _TryDequeueDueMessage(out TimeSpan? delay)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                delay = null;
                return null;
            }

            var topMessage = _queue.Min;
            var ticksUntilDue = topMessage.Item1 - timeProvider.GetUtcNow().UtcDateTime.Ticks;

            if (ticksUntilDue > 0)
            {
                delay = TimeSpan.FromTicks(ticksUntilDue);
                return null;
            }

            _queue.Remove(topMessage);
            delay = null;

            return topMessage.Item2;
        }
    }

    private async Task _WaitUntilDueOrChangedAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = timeProvider.Delay(delay, waitCts.Token);
        var signalTask = _changedSignal.WaitAsync(waitCts.Token);
        var completedTask = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
        await waitCts.CancelAsync().ConfigureAwait(false);

        await completedTask.ConfigureAwait(false);
    }
}
