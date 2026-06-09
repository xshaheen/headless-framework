// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

internal sealed class CommitScope(
    CommitCoordinator coordinator,
    IServiceProvider services,
    IDisposable ambientHandle
) : ICommitScope
{
    private int _signaled;
    private int _disposed;

    public ICommitCoordinator Coordinator => coordinator;

    public async ValueTask SignalAsync(CommitOutcome outcome, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _signaled, 1) == 1)
        {
            return;
        }

        await coordinator.SignalAsync(outcome, services, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (Volatile.Read(ref _signaled) == 0)
        {
            SignalAsync(CommitOutcome.RolledBack, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        coordinator.DisposePromotedRegistrations();
        ambientHandle.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        ambientHandle.Dispose();

        if (Volatile.Read(ref _signaled) == 0)
        {
            return _RollbackAndDisposePromotedRegistrationsAsync();
        }

        coordinator.DisposePromotedRegistrations();

        return ValueTask.CompletedTask;
    }

    private async ValueTask _RollbackAndDisposePromotedRegistrationsAsync()
    {
        try
        {
            await coordinator.SignalAsync(CommitOutcome.RolledBack, services, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            coordinator.DisposePromotedRegistrations();
        }
    }
}
