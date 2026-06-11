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
        var signalStarted = Volatile.Read(ref _signalStarted) == 1;

        try
        {
            innerDispose = inner.DisposeAsync();
        }
        catch
        {
            detach(this);
            throw;
        }

        return _FinishDisposeAsync(innerDispose, signalStarted);
    }

    private async ValueTask _FinishDisposeAsync(ValueTask innerDispose, bool signalStarted)
    {
        try
        {
            await innerDispose.ConfigureAwait(false);
        }
        finally
        {
            detach(this);

            if (!signalStarted)
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
