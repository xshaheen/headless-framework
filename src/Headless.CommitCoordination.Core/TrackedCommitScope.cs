// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.CommitCoordination;

internal sealed class TrackedCommitScope(
    ICommitScope inner,
    Action<ICommitScope> detach,
    AsyncServiceScope? ownedServices = null
) : ICommitScope
{
    private int _disposed;
    private int _signalStarted;
    private int _ownedServicesDisposed;

    public ICommitCoordinator Coordinator => inner.Coordinator;

    public async ValueTask SignalAsync(CommitOutcome outcome, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _signalStarted, 1);

        try
        {
            await inner.SignalAsync(outcome, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            detach(this);
            await _DisposeOwnedServicesAsync().ConfigureAwait(false);
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
            inner.Dispose();
        }
        finally
        {
            detach(this);

            if (Volatile.Read(ref _signalStarted) == 0)
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
            innerDispose = inner.DisposeAsync();
        }
        catch
        {
            detach(this);
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
            detach(this);

            // Re-read _signalStarted AFTER the inner dispose, not from a snapshot taken before it: a SignalAsync may
            // have started concurrently and claimed the drain, which owns these services for its lifetime and disposes
            // them in its own finally. Only dispose here when no signal started; _ownedServicesDisposed is the final
            // double-dispose guard.
            if (Volatile.Read(ref _signalStarted) == 0)
            {
                await _DisposeOwnedServicesAsync().ConfigureAwait(false);
            }
        }
    }

    private void _DisposeOwnedServices()
    {
        if (ownedServices is null || Interlocked.Exchange(ref _ownedServicesDisposed, 1) == 1)
        {
            return;
        }

        ownedServices.Value.Dispose();
    }

    private async ValueTask _DisposeOwnedServicesAsync()
    {
        if (ownedServices is null || Interlocked.Exchange(ref _ownedServicesDisposed, 1) == 1)
        {
            return;
        }

        await ownedServices.Value.DisposeAsync().ConfigureAwait(false);
    }
}
