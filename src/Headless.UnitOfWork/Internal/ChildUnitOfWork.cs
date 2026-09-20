// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork.Internal;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// A child view over the root unit's engine, returned when a begin runs again on the same resource while a
/// unit is active. Registrations made through this view are tagged so <c>CompleteAsync</c> keeps them (they
/// transfer to the root and drain at the root's completion) and an abandon drops them and aborts the root.
/// </summary>
/// <remarks>
/// The child is a <i>view</i>, not a second engine: it registers on the root engine but tracks every handle it
/// created, so it can drop exactly its own registrations on abandon. The commit verb stays on the root; a
/// child's <see cref="CompleteAsync" /> only transfers.
/// </remarks>
internal sealed class ChildUnitOfWork(Internal.UnitOfWork root, UnitOfWorkManager manager) : IUnitOfWork
{
    private readonly Lock _gate = new();
    private List<Internal.UnitOfWork.CompletedRegistration> _completed = [];
    private List<Internal.UnitOfWork.FailedRegistration> _failed = [];
    private int _disposed;
    private int _completedView;

    /// <summary>The root engine this view registers on; lets another scope's manager adopt it as a joinable frame.</summary>
    internal Internal.UnitOfWork Engine => root;

    // Views onto the root engine.
    public UnitOfWorkState State => root.State;

    public UnitOfWorkFailure? Failure => root.Failure;

    public IUnitOfWorkResource? Resource => root.Resource;

    public bool IsRetryPrevented => root.IsRetryPrevented;

    public IDisposable OnCompleted(Func<ValueTask> work)
    {
        _ThrowIfDisposed();

        var registration = (Internal.UnitOfWork.CompletedRegistration)root.OnCompleted(work);

        lock (_gate)
        {
            _completed.Add(registration);
        }

        return registration;
    }

    public IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work)
    {
        _ThrowIfDisposed();

        var registration = (Internal.UnitOfWork.FailedRegistration)root.OnFailed(work);

        lock (_gate)
        {
            _failed.Add(registration);
        }

        return registration;
    }

    public TState GetOrAdd<TState>(Func<IUnitOfWork, TState> factory)
        where TState : class
    {
        _ThrowIfDisposed();

        return root.GetOrAdd(this, factory);
    }

    public TState GetOrAdd<TState, TArg>(TArg arg, Func<IUnitOfWork, TArg, TState> factory)
        where TState : class
    {
        _ThrowIfDisposed();

        return root.GetOrAdd(this, arg, factory);
    }

    public TFeature? GetFeature<TFeature>()
        where TFeature : class
    {
        ThrowIfUnusable();

        // The feature is cached on the root engine and handed the root view: this child can complete while the
        // unit stays open, and the one cached instance must outlive it.
        return manager.GetFeature<TFeature>(root);
    }

    /// <summary>
    /// Answers for the view, not the unit: <see cref="State" /> forwards to the root, so a view that has
    /// already completed still reports <see cref="UnitOfWorkState.Active" /> while the root is open.
    /// </summary>
    public void ThrowIfUnusable()
    {
        _ThrowIfDisposed();

        if (Volatile.Read(ref _completedView) == 1)
        {
            throw new InvalidOperationException(
                "This nested unit of work has already completed. Use the unit of work it was begun under, or begin a new one."
            );
        }

        root.ThrowIfNotActive();
    }

    public void PreventRetry() => root.PreventRetry();

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();

        // Transfer: the registrations already sit on the root engine; keep them and pop the child. Once
        // completed, the view is terminal — a later rollback or dispose is the documented no-op and must not
        // be mistaken for an abandon that would abort the root.
        _ = cancellationToken; // reserved: a child completion currently performs no resource work

        if (Interlocked.CompareExchange(ref _completedView, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "The unit of work has already completed. Begin a new unit of work for further work."
            );
        }

        await manager.CompleteChildAsync(root, this).ConfigureAwait(false);
    }

    public ValueTask RollbackAsync()
    {
        _ThrowIfDisposed();

        if (Volatile.Read(ref _completedView) == 1)
        {
            return ValueTask.CompletedTask; // A terminal view ignores the conflicting verb.
        }

        // A child rollback aborts the root (never a silent poison): the root's registrations fail with
        // ChildAbandoned and the child's own registrations drop. The explicit verb surfaces a rollback fault.
        return manager.AbandonChildAsync(root, this, propagateFaults: true);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0 || Volatile.Read(ref _completedView) == 1)
        {
            return;
        }

        manager.AbandonChild(root, this);
    }

    public ValueTask DisposeAsync() => _DisposeAsyncCore();

    private async ValueTask _DisposeAsyncCore()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0 || Volatile.Read(ref _completedView) == 1)
        {
            return;
        }

        // An implicit dispose never throws: it usually runs while the caller unwinds its own exception.
        await manager.AbandonChildAsync(root, this, propagateFaults: false).ConfigureAwait(false);
    }

    /// <summary>Drops exactly this child's registrations from the root engine (an abandon never transfers).</summary>
    internal void DropRegistrations()
    {
        lock (_gate)
        {
            foreach (var registration in _completed)
            {
                registration.Dispose();
            }

            foreach (var registration in _failed)
            {
                registration.Dispose();
            }

            _completed = [];
            _failed = [];
        }
    }

    private void _ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
