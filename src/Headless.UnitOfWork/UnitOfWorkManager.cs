// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.UnitOfWork.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.UnitOfWork;

/// <summary>
/// The scoped unit-of-work manager: one slot per DI scope, a plain <see cref="Current" /> field (no
/// <c>AsyncLocal</c>), an in-flight begin latch, join-by-default nesting with child views, and leak detection
/// on scope disposal. Created by DI through <c>AddUnitOfWork()</c>; tests construct it directly.
/// </summary>
/// <param name="logger">Logger for the leak warning and the forgotten-completion warning.</param>
internal sealed partial class UnitOfWorkManager(ILogger<UnitOfWorkManager>? logger = null)
    : IUnitOfWorkManager,
        IDisposable,
        IAsyncDisposable
{
    private const string _ConcurrentBeginMessage =
        "Another unit of work is being begun concurrently in this scope. Await the first BeginAsync before beginning again, or run parallel work in separate service scopes.";

    private const string _AnotherResourceMessage =
        "A unit of work is already active on another resource in this scope. Complete it first, or run the second operation in its own service scope (IServiceScopeFactory.CreateScope()).";

    private const string _NestedStillActiveMessage =
        "A nested unit of work begun in this scope is still active. Complete or dispose it before completing the root.";

    private const string _NestedAbandonedMessage =
        "A nested unit of work was disposed without completing, so the root cannot complete; the transaction is rolled back.";

    private readonly Lock _gate = new();
    private readonly List<Frame> _frames = []; // bottom .. top; the top frame is the innermost unit.
    private bool _beginning;
    private bool _disposed;

    /// <inheritdoc />
    public IUnitOfWork? Current { get; private set; }

    /// <summary>The manager's logger, shared with the provider runners so post-commit drain faults land in one category.</summary>
    internal ILogger Logger { get; } = logger ?? NullLogger<UnitOfWorkManager>.Instance;

    /// <inheritdoc />
    public ValueTask<IUnitOfWork> BeginAsync(
        UnitOfWorkOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_beginning)
            {
                throw new InvalidOperationException(_ConcurrentBeginMessage);
            }

            if (_frames.Count > 0)
            {
                // Join by default: a begin under an active unit is a child view over the same engine.
                var child = _OpenChild(_frames[^1]);

                return ValueTask.FromResult<IUnitOfWork>(child);
            }

            // A resource-less root needs no factory: the slot is claimed and published synchronously.
            var root = new Internal.UnitOfWork(resource: null, Logger);
            var handle = new UnitOfWorkHandle(root, this);

            _frames.Add(new Frame(root, handle));
            Current = handle;

            return ValueTask.FromResult<IUnitOfWork>(handle);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IUnitOfWork> BeginAsync(
        Func<CancellationToken, ValueTask<IUnitOfWorkResource>> beginResource,
        UnitOfWorkOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(beginResource);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_beginning)
            {
                throw new InvalidOperationException(_ConcurrentBeginMessage);
            }

            // The latch is claimed synchronously before the first await; whether the begin joins as a child,
            // nests independently, or throws depends on the resource identity, which only the factory reveals.
            _beginning = true;
        }

        IUnitOfWorkResource resource;

        try
        {
            resource = await beginResource(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A faulted resource begin releases the slot; the fault propagates as-is, never downgraded.
            lock (_gate)
            {
                _beginning = false;
            }

            throw;
        }

        lock (_gate)
        {
            _beginning = false;

            if (_frames.Count > 0)
            {
                var frame = _frames[^1];
                var activeResource = frame.Engine.Resource;

                if (activeResource == resource)
                {
                    // Same resource: a child view over the root engine. The factory returned the live
                    // resource, so no second transaction was begun.
                    return _OpenChild(frame);
                }

                if (activeResource is not null)
                {
                    throw new InvalidOperationException(_AnotherResourceMessage);
                }

                // A resource-bearing begin under a resource-less root is an independent nested unit with its
                // own commit and drain; registrations are not transferred.
            }

            var unit = new Internal.UnitOfWork(resource, Logger);
            var handle = new UnitOfWorkHandle(unit, this);

            _frames.Add(new Frame(unit, handle));
            Current = handle;

            return handle;
        }
    }

    /// <inheritdoc />
    public IUnitOfWork Enlist(IUnitOfWorkResource resource, UnitOfWorkOptions? options = null)
    {
        Argument.IsNotNull(resource);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_beginning)
            {
                throw new InvalidOperationException(_ConcurrentBeginMessage);
            }

            if (_frames.Count > 0)
            {
                var frame = _frames[^1];
                var activeResource = frame.Engine.Resource;

                if (activeResource == resource)
                {
                    var child = _OpenChild(frame);

                    return child;
                }

                if (activeResource is not null)
                {
                    throw new InvalidOperationException(_AnotherResourceMessage);
                }

                // A resource-bearing enlist under a resource-less root is an independent nested unit.
            }

            var unit = new Internal.UnitOfWork(resource, Logger);
            var handle = new UnitOfWorkHandle(unit, this);

            _frames.Add(new Frame(unit, handle));
            Current = handle;

            return handle;
        }
    }

    /// <inheritdoc />
    public IDisposable Adopt(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (ReferenceEquals(Current, unitOfWork))
            {
                // Re-entrant adoption (a handler re-entering the save pipeline on the same context).
                return NoOpAdoption.Instance;
            }

            if (_beginning || Current is not null)
            {
                throw new InvalidOperationException(_ConcurrentBeginMessage);
            }

            // An adopted unit becomes a joinable frame: a same-resource begin or enlist in this scope opens a child
            // view over the foreign engine instead of a second root, exactly as it would in the owning scope. The
            // frame is marked so scope disposal never claims a unit another scope owns.
            var engine = unitOfWork switch
            {
                UnitOfWorkHandle handle => handle.Engine,
                ChildUnitOfWork child => child.Engine,
                _ => null,
            };

            if (engine is not null)
            {
                _frames.Add(new Frame(engine, unitOfWork) { Adopted = true });
            }

            Current = unitOfWork;

            return new Adoption(this, unitOfWork, engine);
        }
    }

    /// <summary>Completes a root or nested unit: claim, commit (owned), drain, pop.</summary>
    internal async ValueTask CompleteRootAsync(Internal.UnitOfWork unit, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Internal.UnitOfWork.UnitOfWorkTerminalClaim claim;

        lock (_gate)
        {
            var frameIndex = _IndexOfFrame(unit);

            if (frameIndex < 0)
            {
                // The frame was already unwound (a child abandon aborted this unit).
                _ThrowForTerminalUnit(unit);
            }

            if (frameIndex != _frames.Count - 1 || _frames[frameIndex].ActiveChildren > 0)
            {
                throw new InvalidOperationException(_NestedStillActiveMessage);
            }

            if (!unit.TryClaimCompleted(out claim))
            {
                // A root aborted by a child abandon still holds its slot; the owner's complete releases it.
                _frames.RemoveAt(frameIndex);
                Current = _frames.Count > 0 ? _frames[^1].Handle : null;
                _ThrowForTerminalUnit(unit);
            }

            // A completed unit frees its slot immediately; the owner's later dispose is a no-op.
            _frames.RemoveAt(frameIndex);
            Current = _frames.Count > 0 ? _frames[^1].Handle : null;
        }

        if (unit.Resource is { IsOwned: true } resource)
        {
            try
            {
                await resource.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The commit faulted: the unit transitions to Failed before the exception propagates, so a
                // second CompleteAsync throws the "already failed" message rather than re-committing.
                var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.Faulted, ex);

                unit.TransitionCompletedToFailed(failure);
                // The owned transaction is still open when the commit never reached the database (an interceptor
                // or a network fault before the commit) and would otherwise hold its locks until the connection
                // dies; roll it back best-effort. A resource whose commit did land reports the transaction as
                // finished and treats this as a dispose.
                await _RollbackAfterCommitFaultAsync(resource).ConfigureAwait(false);
                await _DrainFailedQuietlyAsync(claim, failure).ConfigureAwait(false);
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        await Internal.UnitOfWork.DrainCompletedAsync(claim).ConfigureAwait(false);
    }

    private async ValueTask _RollbackAfterCommitFaultAsync(IUnitOfWorkResource resource)
    {
        try
        {
            await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The commit fault is the caller's outcome; a rollback fault on top of it is logged, never masks it.
            LogCommitFaultRollbackFaulted(Logger, ex);
        }
    }

    /// <summary>
    /// Completes a child view: its registrations already live on the root engine, so completing keeps them
    /// there (the transfer) and they drain when the root completes.
    /// </summary>
    internal ValueTask CompleteChildAsync(Internal.UnitOfWork root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            var frameIndex = _IndexOfFrame(root);

            if (frameIndex >= 0)
            {
                var frame = _frames[frameIndex];

                if (frame.ActiveChildren > 0)
                {
                    frame.ActiveChildren--;
                }

                Current = frame.Handle;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Rolls a root/nested unit back explicitly: claim Failed(RolledBack), roll the owned resource back, drain.</summary>
    internal async ValueTask RollbackUnitAsync(Internal.UnitOfWork unit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.RolledBack);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            _PopFrame(unit); // Idempotent: a terminal unit ignores the conflicting verb but releases its slot.

            return;
        }

        _PopFrame(unit);
        await _RollBackAndDrainAsync(unit, claim, failure).ConfigureAwait(false);
    }

    /// <summary>Abandons a child view: drops its registrations and aborts the root (async path).</summary>
    internal async ValueTask AbandonChildAsync(Internal.UnitOfWork root, ChildUnitOfWork child)
    {
        var aborted = _AbandonChildClaim(root, child);

        if (aborted is null)
        {
            return;
        }

        var (claim, failure) = aborted.Value;

        await _RollBackAndDrainAsync(root, claim, failure).ConfigureAwait(false);
    }

    /// <summary>
    /// Abandons a child view synchronously: the claim and the registration drop are synchronous; the rollback
    /// and the drain are offloaded so a captured SynchronizationContext cannot deadlock the disposing thread.
    /// </summary>
    internal void AbandonChild(Internal.UnitOfWork root, ChildUnitOfWork child)
    {
        var aborted = _AbandonChildClaim(root, child);

        if (aborted is null)
        {
            return;
        }

        var (claim, failure) = aborted.Value;

        _RunBackground(() => _RollBackAndDrainAsync(root, claim, failure).AsTask());
    }

    /// <summary>
    /// Disposes a root/nested handle without a completion verb: the claim pops the frame synchronously; the rollback
    /// and the drain run observed in the background so neither a synchronous disposer nor an async one blocks on
    /// the drain's scope-state disposal (a captured SynchronizationContext could otherwise deadlock).
    /// </summary>
    internal void DisposeUnit(Internal.UnitOfWork unit)
    {
        if (_disposed)
        {
            return; // The manager's own disposal already unwound every active unit.
        }

        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.Abandoned);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            _PopFrame(unit); // A dispose after CompleteAsync or RollbackAsync is a no-op; a child-aborted root frees its slot.

            return;
        }

        _PopFrame(unit);
        _WarnForgottenCompletion(unit.Resource);
        _RunBackground(() => _RollBackAndDrainAsync(unit, claim, failure).AsTask());
    }

    internal async ValueTask DisposeUnitAsync(Internal.UnitOfWork unit)
    {
        if (_disposed)
        {
            return;
        }

        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.Abandoned);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            _PopFrame(unit); // A dispose after a terminal verb is a no-op; a child-aborted root frees its slot.

            return;
        }

        _PopFrame(unit);
        _WarnForgottenCompletion(unit.Resource);
        await _RollBackAndDrainAsync(unit, claim, failure).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the manager synchronously (a scope may be disposed on a synchronous path): the claims and the
    /// leak warnings are synchronous; each leaked unit's rollback and failure drain run observed in the
    /// background so a captured SynchronizationContext can neither deadlock nor stall the disposing thread.
    /// </summary>
    public void Dispose()
    {
        var drained = _ClaimLeakedUnits();

        foreach (var (unit, claim, failure) in drained)
        {
            _RunBackground(async () =>
            {
                if (unit.Resource is { IsOwned: true } resource)
                {
                    try
                    {
                        await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogScopeDisposedRollbackFaulted(Logger, ex);
                    }
                }

                await _DrainFailedQuietlyAsync(claim, failure).ConfigureAwait(false);
            });
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var drained = _ClaimLeakedUnits();

        foreach (var (unit, claim, failure) in drained)
        {
            if (unit.Resource is { IsOwned: true } resource)
            {
                try
                {
                    await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogScopeDisposedRollbackFaulted(Logger, ex);
                }
            }

            await _DrainFailedQuietlyAsync(claim, failure).ConfigureAwait(false);
        }
    }

    private List<(
        Internal.UnitOfWork Unit,
        Internal.UnitOfWork.UnitOfWorkTerminalClaim Claim,
        UnitOfWorkFailure Failure
    )> _ClaimLeakedUnits()
    {
        List<(Internal.UnitOfWork, Internal.UnitOfWork.UnitOfWorkTerminalClaim, UnitOfWorkFailure)> drained = [];

        lock (_gate)
        {
            if (_disposed)
            {
                return drained;
            }

            _disposed = true;

            // Unwind innermost-first: every still-active unit fails with ScopeDisposed. An adopted frame belongs to
            // another scope's manager, which owns its lifecycle; it is dropped from this slot without a claim.
            for (var i = _frames.Count - 1; i >= 0; i--)
            {
                if (_frames[i].Adopted)
                {
                    continue;
                }

                var unit = _frames[i].Engine;
                var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.ScopeDisposed);

                if (unit.TryClaimFailed(failure, out var claim))
                {
                    drained.Add((unit, claim, failure));
                    LogScopeDisposedLeak(Logger);
                }
            }

            _frames.Clear();
            Current = null;
        }

        return drained;
    }

    /// <summary>The failure path shared by rollback, abandon, and dispose: roll the owned resource back, then drain.</summary>
    private static async ValueTask _RollBackAndDrainAsync(
        Internal.UnitOfWork unit,
        Internal.UnitOfWork.UnitOfWorkTerminalClaim claim,
        UnitOfWorkFailure failure
    )
    {
        if (unit.Resource is { IsOwned: true } resource)
        {
            await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _DrainFailedAsync(claim, failure).ConfigureAwait(false);
    }

    private (Internal.UnitOfWork.UnitOfWorkTerminalClaim Claim, UnitOfWorkFailure Failure)? _AbandonChildClaim(
        Internal.UnitOfWork root,
        ChildUnitOfWork child
    )
    {
        // Drop the child's registrations before anything else: an abandoned child's work never transfers.
        child.DropRegistrations();

        lock (_gate)
        {
            var frameIndex = _IndexOfFrame(root);

            if (frameIndex >= 0)
            {
                var frame = _frames[frameIndex];

                if (frame.ActiveChildren > 0)
                {
                    frame.ActiveChildren--;
                }

                Current = frame.Handle;
            }
        }

        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.ChildAbandoned);

        if (!root.TryClaimFailed(failure, out var claim))
        {
            return null; // The root already reached its terminal state; the child's dispose is a no-op.
        }

        // The aborted root keeps its slot: Current still answers with the handle the owner holds, and the
        // owner's own CompleteAsync / RollbackAsync / dispose is what unwinds the frame — with the nested-abandon
        // message on the complete path, so the abort is never silent.
        return (claim, failure);
    }

    private ChildUnitOfWork _OpenChild(Frame frame)
    {
        frame.ActiveChildren++;

        var child = new ChildUnitOfWork(frame.Engine, this);
        Current = child;

        return child;
    }

    private void _PopFrame(Internal.UnitOfWork unit)
    {
        lock (_gate)
        {
            var index = _IndexOfFrame(unit);

            if (index < 0)
            {
                return;
            }

            _frames.RemoveAt(index);
            Current = _frames.Count > 0 ? _frames[^1].Handle : null;
        }
    }

    // A plain loop: this runs on every complete/rollback/dispose, the list holds a handful of frames at most, and a
    // FindIndex lambda would allocate a closure over the engine each time. Callers hold _gate.
    private int _IndexOfFrame(Internal.UnitOfWork engine)
    {
        for (var i = 0; i < _frames.Count; i++)
        {
            if (ReferenceEquals(_frames[i].Engine, engine))
            {
                return i;
            }
        }

        return -1;
    }

    private void _RunBackground(Func<Task> work)
    {
        BackgroundFault.Observe(
            Task.Run(work),
            Logger,
            static (logger, exception) => LogBackgroundDrainFaulted(logger, exception.InnerException)
        );
    }

    private static async ValueTask _DrainFailedAsync(
        Internal.UnitOfWork.UnitOfWorkTerminalClaim claim,
        UnitOfWorkFailure failure
    )
    {
        await Internal.UnitOfWork.DrainFailedAsync(claim, failure).ConfigureAwait(false);
    }

    private async ValueTask _DrainFailedQuietlyAsync(
        Internal.UnitOfWork.UnitOfWorkTerminalClaim claim,
        UnitOfWorkFailure failure
    )
    {
        try
        {
            await Internal.UnitOfWork.DrainFailedAsync(claim, failure).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failure-drain fault (a scope-state disposal fault) must not mask the caller's own exception.
            LogBackgroundDrainFaulted(Logger, ex);
        }
    }

    private void _WarnForgottenCompletion(IUnitOfWorkResource? resource)
    {
        // Only observed mode: the unit was neither completed nor rolled back (this is the abandon path) and
        // the caller's transaction already finished, so the after-commit work was silently discarded.
        if (resource is { IsOwned: false, IsTransactionCompleted: true })
        {
            LogForgottenCompletion(Logger);
        }
    }

    private static void _ThrowForTerminalUnit(Internal.UnitOfWork unit)
    {
        if (unit.State == UnitOfWorkState.Completed)
        {
            throw new InvalidOperationException(
                "The unit of work has already completed. Begin a new unit of work for further work."
            );
        }

        if (unit.Failure?.Reason == UnitOfWorkFailureReason.ChildAbandoned)
        {
            throw new InvalidOperationException(_NestedAbandonedMessage);
        }

        throw new InvalidOperationException(
            $"The unit of work has already failed ({unit.Failure?.Reason}) and cannot be completed. Begin a new unit of work."
        );
    }

    private sealed class Frame(Internal.UnitOfWork engine, IUnitOfWork handle)
    {
        public Internal.UnitOfWork Engine { get; } = engine;

        public IUnitOfWork Handle { get; } = handle;

        public int ActiveChildren { get; set; }

        /// <summary>True when the frame mirrors a unit owned by another scope's manager (see <see cref="Adopt" />).</summary>
        public bool Adopted { get; init; }
    }

    private sealed class NoOpAdoption : IDisposable
    {
        public static readonly NoOpAdoption Instance = new();

        public void Dispose() { }
    }

    private sealed class Adoption(UnitOfWorkManager manager, IUnitOfWork adopted, Internal.UnitOfWork? engine)
        : IDisposable
    {
        public void Dispose()
        {
            lock (manager._gate)
            {
                if (engine is not null)
                {
                    manager._frames.RemoveAll(f => f.Adopted && ReferenceEquals(f.Engine, engine));
                }

                if (ReferenceEquals(manager.Current, adopted) || manager._frames.Count == 0)
                {
                    manager.Current = manager._frames.Count > 0 ? manager._frames[^1].Handle : null;
                }
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "A unit of work begun with BeginAsync was still active when its service scope was disposed; it was rolled back. Complete or dispose every unit of work before the scope ends."
    )]
    private static partial void LogScopeDisposedLeak(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "A unit of work enlisted with Enlist(...) was disposed without CompleteAsync or RollbackAsync after its transaction completed; the after-commit work was discarded and durable rows will be recovered by the relay."
    )]
    private static partial void LogForgottenCompletion(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Rolling back a leaked unit of work faulted.")]
    private static partial void LogScopeDisposedRollbackFaulted(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "A unit-of-work background drain faulted.")]
    private static partial void LogBackgroundDrainFaulted(ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Rolling back a unit of work whose commit faulted failed as well; its transaction may still be open."
    )]
    private static partial void LogCommitFaultRollbackFaulted(ILogger logger, Exception exception);
}
