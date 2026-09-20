// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork.Internal;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The public root/nested handle over the internal <see cref="Internal.UnitOfWork" /> engine. Owns the
/// lifecycle verbs (<see cref="CompleteAsync" />, <see cref="RollbackAsync" />, dispose) and enforces the
/// catalogued transition messages. Created only by <see cref="UnitOfWorkManager" />.
/// </summary>
internal sealed class UnitOfWorkHandle : IUnitOfWork
{
    private readonly UnitOfWorkManager _manager;
    private int _disposed;

    public UnitOfWorkHandle(Internal.UnitOfWork unit, UnitOfWorkManager manager)
    {
        Engine = unit;
        _manager = manager;
        // This handle is the engine's only root view and lives exactly as long as the unit, so the engine hands
        // it — never a child view — to a feature factory. Attaching here keeps the invariant at the one place a
        // root handle can come into existence.
        unit.AttachRootView(this);
    }

    /// <summary>The engine behind this handle; lets another scope's manager adopt it as a joinable frame.</summary>
    internal Internal.UnitOfWork Engine { get; }

    public UnitOfWorkState State => Engine.State;

    public UnitOfWorkFailure? Failure => Engine.Failure;

    public IUnitOfWorkResource? Resource => Engine.Resource;

    public bool IsRetryPrevented => Engine.IsRetryPrevented;

    public IDisposable OnCompleted(Func<ValueTask> work)
    {
        _ThrowIfDisposed();

        return Engine.OnCompleted(work);
    }

    public IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work)
    {
        _ThrowIfDisposed();

        return Engine.OnFailed(work);
    }

    public TState GetOrAdd<TState>(Func<IUnitOfWork, TState> factory)
        where TState : class
    {
        _ThrowIfDisposed();

        return Engine.GetOrAdd(this, factory);
    }

    public TState GetOrAdd<TState, TArg>(TArg arg, Func<IUnitOfWork, TArg, TState> factory)
        where TState : class
    {
        _ThrowIfDisposed();

        return Engine.GetOrAdd(this, arg, factory);
    }

    public TFeature? GetFeature<TFeature>()
        where TFeature : class
    {
        ThrowIfUnusable();

        return _manager.GetFeature<TFeature>(Engine);
    }

    /// <summary>The root handle carries work for as long as the unit itself does, so the engine answers for it.</summary>
    public void ThrowIfUnusable()
    {
        _ThrowIfDisposed();
        Engine.ThrowIfNotActive();
    }

    public void PreventRetry() => Engine.PreventRetry();

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();

        // The manager owns the slot bookkeeping (child checks, Current restore) around the claim and drain.
        await _manager.CompleteRootAsync(Engine, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RollbackAsync()
    {
        _ThrowIfDisposed();

        return _manager.RollbackUnitAsync(Engine);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _manager.DisposeUnit(Engine);
    }

    public ValueTask DisposeAsync() => _DisposeAsyncCore();

    private async ValueTask _DisposeAsyncCore()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        await _manager.DisposeUnitAsync(Engine).ConfigureAwait(false);
    }

    private void _ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
