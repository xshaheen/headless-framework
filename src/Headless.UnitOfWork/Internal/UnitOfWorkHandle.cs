// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork.Internal;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The public handle over the internal <see cref="Internal.UnitOfWork" /> engine. Owns the lifecycle verbs
/// (<see cref="CompleteAsync" />, <see cref="RollbackAsync" />, dispose) and enforces the catalogued transition
/// messages. Created only by <see cref="UnitOfWorkFactory" />.
/// </summary>
internal sealed class UnitOfWorkHandle(Internal.UnitOfWork unit, UnitOfWorkFactory factory) : IUnitOfWork
{
    private int _disposed;

    public UnitOfWorkState State => unit.State;

    public UnitOfWorkFailure? Failure => unit.Failure;

    public IUnitOfWorkResource? Resource => unit.Resource;

    public bool IsRetryPrevented => unit.IsRetryPrevented;

    public IDisposable OnCompleted(Func<ValueTask> work)
    {
        _ThrowIfDisposed();

        return unit.OnCompleted(work);
    }

    public IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work)
    {
        _ThrowIfDisposed();

        return unit.OnFailed(work);
    }

    public TState GetOrAdd<TState>(Func<IUnitOfWork, TState> factory)
        where TState : class
    {
        _ThrowIfDisposed();

        return unit.GetOrAdd(this, factory);
    }

    public TState GetOrAdd<TState, TArg>(TArg arg, Func<IUnitOfWork, TArg, TState> factory)
        where TState : class
    {
        _ThrowIfDisposed();

        return unit.GetOrAdd(this, arg, factory);
    }

    public TFeature? GetFeature<TFeature>()
        where TFeature : class, IUnitOfWorkFeature
    {
        _ThrowIfDisposed();

        return factory.GetFeature<TFeature>();
    }

    public void PreventRetry() => unit.PreventRetry();

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();

        await factory.CompleteAsync(unit, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RollbackAsync()
    {
        _ThrowIfDisposed();

        return factory.RollbackAsync(unit);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        factory.Dispose(unit);
    }

    public ValueTask DisposeAsync() => _DisposeAsyncCore();

    private async ValueTask _DisposeAsyncCore()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        await factory.DisposeAsync(unit).ConfigureAwait(false);
    }

    private void _ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
