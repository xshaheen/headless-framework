// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeLeaseHandle(TimeProvider timeProvider) : LeaseMonitor.ILeaseHandle
{
    private readonly Queue<object> _results = [];

    public string Resource { get; init; } = "test-resource";

    public string LeaseId { get; init; } = "test-lock";

    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan MonitoringCadence { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The acquire send time; defaults to the moment the fake is built, as a provider would record it.</summary>
    public long AcquireTimestamp { get; init; } = timeProvider.GetTimestamp();

    public TimeSpan StorageDeadline { get; init; } = TimeSpan.FromSeconds(5);

    public int InvocationCount { get; private set; }

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
        InvocationCount++;

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
