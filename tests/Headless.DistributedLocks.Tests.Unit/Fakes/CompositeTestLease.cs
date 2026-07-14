// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.Fakes;

/// <summary>
/// Configurable <see cref="IDistributedLease"/> fake for composite-acquisition tests. Records release/dispose
/// ordering into a shared event list and can simulate renewal failure, cleanup faults, and lease loss — the
/// behaviors the composite coordinator has to react to. Shared by the mutex, reader-writer, and semaphore suites.
/// </summary>
internal sealed class CompositeTestLease : IDistributedLease
{
    private readonly List<string>? _events;
    private readonly CancellationTokenSource? _lostSource;
    private readonly bool _renewResult;
    private readonly Exception? _renewalException;
    private readonly Func<TimeSpan?, CancellationToken, Task<bool>>? _renewal;
    private readonly Exception? _releaseException;
    private readonly Exception? _disposeException;
    private readonly int? _markLostOnTokenRead;
    private int _lostTokenReads;

    public CompositeTestLease(
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
    )
    {
        Resource = resource;
        LeaseId = $"lease-{resource}";
        FencingToken = fencingToken;
        _events = events;
        CanObserveLoss = canObserveLoss;
        _renewResult = renewResult;
        _renewalException = renewalException;
        _renewal = renewal;
        _releaseException = releaseException;
        _disposeException = disposeException;
        _markLostOnTokenRead = markLostOnTokenRead;
        _lostSource = canObserveLoss ? new CancellationTokenSource() : null;
    }

    public string LeaseId { get; }

    public long? FencingToken { get; }

    public string Resource { get; }

    public int RenewalCount { get; private set; }

    public DateTimeOffset DateAcquired { get; } = DateTimeOffset.UnixEpoch;

    public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

    public CancellationToken LostToken
    {
        get
        {
            if (_markLostOnTokenRead == Interlocked.Increment(ref _lostTokenReads))
            {
                _lostSource!.Cancel();
            }

            return _lostSource?.Token ?? CancellationToken.None;
        }
    }

    public bool CanObserveLoss { get; }

    public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenewalCount++;

        if (_renewal is not null)
        {
            return _renewal(timeUntilExpires, cancellationToken);
        }

        if (_renewalException is not null)
        {
            return Task.FromException<bool>(_renewalException);
        }

        return Task.FromResult(_renewResult);
    }

    public Task ReleaseAsync()
    {
        _events?.Add($"release:{Resource}");
        return _releaseException is null ? Task.CompletedTask : Task.FromException(_releaseException);
    }

    public ValueTask DisposeAsync()
    {
        _events?.Add($"dispose:{Resource}");
        _lostSource?.Dispose();
        return _disposeException is null ? ValueTask.CompletedTask : ValueTask.FromException(_disposeException);
    }

    public void MarkLost() => _lostSource!.Cancel();
}
