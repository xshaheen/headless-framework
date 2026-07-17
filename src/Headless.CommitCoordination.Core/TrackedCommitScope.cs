// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.CommitCoordination;

internal sealed class TrackedCommitScope : ICommitScope
{
    private readonly ICommitScope _inner;
    private readonly Action<ICommitScope> _detach;
    private readonly AsyncServiceScope? _ownedServices;
    private readonly bool _abandonCleanupTransferred;
    private readonly Lock _gate = new();
    private int _disposed;
    private int _signalStarted;
    private int _ownedServicesDisposed;

    internal TrackedCommitScope(
        ICommitScope inner,
        Action<ICommitScope> detach,
        AsyncServiceScope? ownedServices = null
    )
    {
        _inner = inner;
        _detach = detach;
        _ownedServices = ownedServices;

        if (ownedServices is not null && inner is CommitScope commitScope)
        {
            // A sync un-signalled Dispose offloads the rollback drain to the background; the drain resolves
            // callbacks from the owned DI scope, so the drain — not Dispose's frame — must own its disposal.
            commitScope.AbandonCleanup = _DisposeOwnedServices;
            _abandonCleanupTransferred = true;
        }
    }

    // _inner is readonly: reading a stable reference's property needs no lock. The _gate only serializes the
    // mutating operations on _inner (Dispose/DisposeAsync/SignalAsync), not this pass-through read.
    // ReSharper disable once InconsistentlySynchronizedField
    public ICommitCoordinator Coordinator => _inner.Coordinator;

    public async ValueTask SignalAsync(CommitOutcome outcome)
    {
        CommitOutcomeValidation.ThrowIfNotTerminal(outcome);

        ValueTask signal;
        var claimedSignal = false;

        try
        {
            lock (_gate)
            {
                var firstSignal = Interlocked.Exchange(ref _signalStarted, 1) == 0;

                if (_inner is CommitScope commitScope)
                {
                    signal = commitScope.SignalAsync(outcome, out claimedSignal);
                }
                else
                {
                    signal = _inner.SignalAsync(outcome);
                    claimedSignal = firstSignal;
                }
            }

            await signal.ConfigureAwait(false);
        }
        finally
        {
            _detach(this);

            // Only the call that claimed the scope-level signal latch owns the owned DI scope: its await spans the
            // full drain, so disposal here is safely AFTER the drain. A redundant signal (e.g. the SqlServer
            // helper's explicit signal after the diagnostic already claimed) no-ops on the latch and must not tear
            // the services down under the winner's in-flight drain; when an un-signalled Dispose claimed the
            // abandon drain instead, ownership was transferred to that drain's cleanup.
            if (claimedSignal)
            {
                await _DisposeOwnedServicesAsync().ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                _inner.Dispose();
            }
        }
        finally
        {
            _detach(this);

            // When ownership was transferred, the inner scope's abandon path (offloaded drain or inline fallback)
            // disposes the owned services; disposing here would race the background rollback drain.
            if (Volatile.Read(ref _signalStarted) == 0 && !_abandonCleanupTransferred)
            {
                _DisposeOwnedServices();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        ValueTask innerDispose;

        try
        {
            lock (_gate)
            {
                innerDispose = _inner.DisposeAsync();
            }
        }
        catch
        {
            _detach(this);
            throw;
        }

        return _FinishDisposeAsync(innerDispose);
    }

    private async ValueTask _FinishDisposeAsync(ValueTask innerDispose)
    {
        try
        {
            await innerDispose.ConfigureAwait(false);
        }
        finally
        {
            _detach(this);

            // Re-read _signalStarted AFTER the inner dispose, not from a snapshot taken before it: a SignalAsync may
            // have started concurrently and claimed the drain, which owns these services for its lifetime and disposes
            // them in its own finally. Only dispose here when no signal started; the async abandon drain was awaited
            // by the inner dispose above, so this runs after it. _ownedServicesDisposed is the final
            // double-dispose guard.
            if (Volatile.Read(ref _signalStarted) == 0)
            {
                await _DisposeOwnedServicesAsync().ConfigureAwait(false);
            }
        }
    }

    private void _DisposeOwnedServices()
    {
        if (_ownedServices is null || Interlocked.Exchange(ref _ownedServicesDisposed, 1) == 1)
        {
            return;
        }

#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
        _ownedServices.Value.Dispose();
#pragma warning restore MA0045
    }

    private async ValueTask _DisposeOwnedServicesAsync()
    {
        if (_ownedServices is null || Interlocked.Exchange(ref _ownedServicesDisposed, 1) == 1)
        {
            return;
        }

        await _ownedServices.Value.DisposeAsync().ConfigureAwait(false);
    }
}
