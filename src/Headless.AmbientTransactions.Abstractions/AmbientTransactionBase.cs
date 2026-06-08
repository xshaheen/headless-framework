// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.AmbientTransactions;

/// <summary>
/// Base implementation for ambient transactions that manage current-transaction wiring and commit-work drains.
/// </summary>
[PublicAPI]
public abstract class AmbientTransactionBase(ICurrentAmbientTransaction currentAmbientTransaction) : IAmbientTransaction
{
    private readonly ConcurrentQueue<Func<CancellationToken, ValueTask>> _commitWork = new();
    private object? _dbTransaction;
    private int _drainStarted;
    private int _discarded;

    /// <inheritdoc />
    public bool AutoCommit { get; set; }

    /// <inheritdoc />
    public virtual object? DbTransaction
    {
        get => _dbTransaction;
        set
        {
            _dbTransaction = value;

            if (value is null)
            {
                if (ReferenceEquals(currentAmbientTransaction.Current, this))
                {
                    currentAmbientTransaction.Current = null;
                }

                return;
            }

            currentAmbientTransaction.Current = this;
        }
    }

    /// <inheritdoc />
    public void RegisterCommitWork(Func<CancellationToken, ValueTask> drain)
    {
        ArgumentNullException.ThrowIfNull(drain);

        if (Volatile.Read(ref _drainStarted) != 0 || Volatile.Read(ref _discarded) != 0)
        {
            throw new InvalidOperationException("Cannot register commit work after the ambient transaction has completed.");
        }

        _commitWork.Enqueue(drain);
    }

    /// <inheritdoc />
    public void CompleteExternally()
    {
        DrainCommitWork();
    }

    /// <inheritdoc />
    public abstract void Commit();

    /// <inheritdoc />
    public abstract Task CommitAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract void Rollback();

    /// <inheritdoc />
    public abstract Task RollbackAsync(CancellationToken cancellationToken = default);

    protected void DrainCommitWork()
    {
        if (!_TryBeginDrain())
        {
            return;
        }

        while (_commitWork.TryDequeue(out var drain))
        {
            drain(CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }

    protected async ValueTask DrainCommitWorkAsync(CancellationToken cancellationToken = default)
    {
        if (!_TryBeginDrain())
        {
            return;
        }

        while (_commitWork.TryDequeue(out var drain))
        {
            await drain(cancellationToken).ConfigureAwait(false);
        }
    }

    protected void DiscardCommitWork()
    {
        Interlocked.Exchange(ref _discarded, 1);

        while (_commitWork.TryDequeue(out _)) { }
    }

    private bool _TryBeginDrain()
    {
        if (Volatile.Read(ref _discarded) != 0)
        {
            return false;
        }

        return Interlocked.Exchange(ref _drainStarted, 1) == 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (DbTransaction is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (DbTransaction is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DbTransaction = null;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            (DbTransaction as IDisposable)?.Dispose();
            DbTransaction = null;
        }
    }
}
