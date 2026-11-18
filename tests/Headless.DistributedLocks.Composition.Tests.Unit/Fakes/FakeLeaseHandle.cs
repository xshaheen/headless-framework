// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeLeaseHandle(TimeProvider timeProvider) : LeaseMonitor.ILeaseHandle
{
    private readonly Queue<object> _results = [];
    private readonly Lock _invocationLock = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _invocationWaiters = [];
    private int _invocationCount;

    public string Resource { get; init; } = "test-resource";

    public string LeaseId { get; init; } = "test-lock";

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan MonitoringCadence { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The acquire send time; defaults to the moment the fake is built, as a provider would record it.</summary>
    public long AcquireTimestamp { get; init; } = timeProvider.GetTimestamp();

    public TimeSpan StorageDeadline { get; init; } = TimeSpan.FromSeconds(5);

    public int InvocationCount
    {
        get
        {
            lock (_invocationLock)
            {
                return _invocationCount;
            }
        }
    }

    /// <summary>Completes once the monitor has probed this handle at least <paramref name="count"/> times.</summary>
    public Task WhenInvokedAsync(int count)
    {
        lock (_invocationLock)
        {
            if (_invocationCount >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _invocationWaiters.Add((count, signal));

            return signal.Task;
        }
    }

    public void Enqueue(LeaseMonitor.LeaseState state)
    {
        _results.Enqueue(state);
    }

    public void Enqueue(Exception exception)
    {
        _results.Enqueue(exception);
    }

    public Task<LeaseMonitor.LeaseState> RenewOrValidateLeaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_invocationLock)
        {
            _invocationCount++;
            _invocationWaiters.RemoveAll(waiter => _invocationCount >= waiter.Count && waiter.Signal.TrySetResult());
        }

        if (_results.Count == 0)
        {
            return Task.FromResult(LeaseMonitor.LeaseState.Held);
        }

        var result = _results.Dequeue();

        return result switch
        {
            LeaseMonitor.LeaseState state => Task.FromResult(state),
            Exception exception => Task.FromException<LeaseMonitor.LeaseState>(exception),
            _ => throw new InvalidOperationException("Unsupported fake lease result."),
        };
    }
}
