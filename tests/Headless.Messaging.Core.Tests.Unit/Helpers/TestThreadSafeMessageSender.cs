using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Tests.Helpers;

public sealed class TestThreadSafeMessageSender : IMessageSender
{
    private readonly ConcurrentQueue<MediumMessage> _messagesInOrder = [];
    private readonly Lock _waitersLock = new();
    private readonly List<(int Target, TaskCompletionSource Tcs)> _waiters = [];

    public Task<OperateResult> SendAsync(MediumMessage message)
    {
        lock (_messagesInOrder)
        {
            _messagesInOrder.Enqueue(message);
        }

        _SignalWaiters();
        return Task.FromResult(OperateResult.Success);
    }

    public Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices)
    {
        return SendAsync(message);
    }

    public int Count => _messagesInOrder.Count;

    public List<MediumMessage> ReceivedMessages => [.. _messagesInOrder];

    public async Task WaitForCountAsync(int target, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Count >= target)
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waitersLock)
        {
            if (Count >= target)
            {
                return;
            }

            _waiters.Add((target, tcs));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
        await tcs.Task.ConfigureAwait(false);
    }

    private void _SignalWaiters()
    {
        var current = Count;
        List<TaskCompletionSource>? toSignal = null;

        lock (_waitersLock)
        {
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Target > current)
                {
                    continue;
                }

                toSignal ??= [];
                toSignal.Add(_waiters[i].Tcs);
                _waiters.RemoveAt(i);
            }
        }

        if (toSignal is null)
        {
            return;
        }

        foreach (var tcs in toSignal)
        {
            tcs.TrySetResult();
        }
    }
}
