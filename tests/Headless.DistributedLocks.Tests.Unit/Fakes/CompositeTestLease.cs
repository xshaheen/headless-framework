// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.Fakes;

/// <summary>
/// Configurable <see cref="IDistributedLease"/> fake for composite-acquisition tests. Records release/dispose
/// ordering into a shared event list and can simulate renewal failure, cleanup faults, and lease loss — the
/// behaviors the composite coordinator has to react to. Shared by the mutex, reader-writer, and semaphore suites.
/// </summary>
internal sealed class CompositeTestLease(
    string resource,
    List<string>? events = null,
    long? fencingToken = null,
    bool canObserveLoss = false,
    bool renewResult = true,
    Exception? renewalException = null,
    Func<TimeSpan?, CancellationToken, Task<bool>>? renewal = null,
    Exception? releaseException = null,
    Exception? disposeException = null,
    int? markLostOnTokenRead = null
) : IDistributedLease
{
    private readonly CancellationTokenSource? _lostSource = canObserveLoss ? new CancellationTokenSource() : null;
    private int _lostTokenReads;

    public string LeaseId { get; } = $"lease-{resource}";

    public long? FencingToken { get; } = fencingToken;

    public string Resource { get; } = resource;

    public int RenewalCount { get; private set; }

    public DateTimeOffset DateAcquired { get; } = DateTimeOffset.UnixEpoch;

    public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

    public CancellationToken LostToken
    {
        get
        {
            if (markLostOnTokenRead == Interlocked.Increment(ref _lostTokenReads))
            {
                _lostSource!.Cancel();
            }

            return _lostSource?.Token ?? CancellationToken.None;
        }
    }

    public bool CanObserveLoss { get; } = canObserveLoss;

    public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenewalCount++;

        if (renewal is not null)
        {
            return renewal(timeUntilExpires, cancellationToken);
        }

        if (renewalException is not null)
        {
            return Task.FromException<bool>(renewalException);
        }

        return Task.FromResult(renewResult);
    }

    public Task ReleaseAsync()
    {
        events?.Add($"release:{Resource}");
        return releaseException is null ? Task.CompletedTask : Task.FromException(releaseException);
    }

    public ValueTask DisposeAsync()
    {
        events?.Add($"dispose:{Resource}");
        _lostSource?.Dispose();
        return disposeException is null ? ValueTask.CompletedTask : ValueTask.FromException(disposeException);
    }

#pragma warning disable MA0045 // Loss must be observed synchronously by the test double.
    public void MarkLost() => _lostSource!.Cancel();
#pragma warning restore MA0045
}
