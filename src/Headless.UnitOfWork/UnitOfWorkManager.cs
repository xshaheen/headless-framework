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

    private readonly ILogger _logger = logger ?? NullLogger<UnitOfWorkManager>.Instance;
    private readonly Lock _gate = new();
    private readonly List<Frame> _frames = []; // bottom .. top; the top frame is the innermost unit.
    private bool _beginning;
    private bool _disposed;

    /// <inheritdoc />
    public IUnitOfWork? Current { get; private set; }

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
            var root = new Internal.UnitOfWork(resource: null, _logger);
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

            var unit = new Internal.UnitOfWork(resource, _logger);
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

            var unit = new Internal.UnitOfWork(resource, _logger);
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

            if (Current is not null)
            {
                throw new InvalidOperationException(_ConcurrentBeginMessage);
            }

            Current = unitOfWork;

            return new Adoption(this, unitOfWork);
        }
    }

    /// <summary>Completes a root or nested unit: claim, commit (owned), drain, pop.</summary>
    internal async ValueTask CompleteRootAsync(Internal.UnitOfWork unit, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Internal.UnitOfWork.UnitOfWorkTerminalClaim claim;

        lock (_gate)
        {
            var frameIndex = _frames.FindIndex(f => ReferenceEquals(f.Engine, unit));

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
                await _DrainFailedQuietlyAsync(claim, failure).ConfigureAwait(false);
                ExceptionDispatchInfo.Capture(ex).Throw();
            }
        }

        await Internal.UnitOfWork.DrainCompletedAsync(claim).ConfigureAwait(false);
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
            var frame = _frames.FirstOrDefault(f => ReferenceEquals(f.Engine, root));

            if (frame is not null)
            {
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
            return; // Idempotent: a terminal unit ignores the conflicting verb.
        }

        _PopFrame(unit);

        if (unit.Resource is { IsOwned: true } resource)
        {
            await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _DrainFailedAsync(claim, failure).ConfigureAwait(false);
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

        if (root.Resource is { IsOwned: true } resource)
        {
            await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _DrainFailedAsync(claim, failure).ConfigureAwait(false);
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

        _RunBackground(async () =>
        {
            if (root.Resource is { IsOwned: true } resource)
            {
                await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await _DrainFailedAsync(claim, failure).ConfigureAwait(false);
        });
    }

    /// <summary>Disposes a root/nested handle synchronously: the claim pops the frame, the drain is offloaded.</summary>
    internal void DisposeUnit(Internal.UnitOfWork unit, bool synchronous)
    {
        if (_disposed)
        {
            return; // The manager's own disposal already unwound every active unit.
        }

        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.Abandoned);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            return; // A dispose after CompleteAsync or RollbackAsync is a no-op.
        }

        _PopFrame(unit);
        _WarnForgottenCompletion(unit.Resource);

        if (synchronous)
        {
            _RunBackground(async () =>
            {
                if (unit.Resource is { IsOwned: true } resource)
                {
                    await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }

                await _DrainFailedAsync(claim, failure).ConfigureAwait(false);
            });

            return;
        }

        // The async dispose path still must not block its caller on the drain's scope-state disposal:
        // run it observed in the background, exactly like the synchronous path.
        _RunBackground(() => _DisposeUnitAsync(unit, claim, failure).AsTask());
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
            return;
        }

        _PopFrame(unit);
        _WarnForgottenCompletion(unit.Resource);
        await _DisposeUnitAsync(unit, claim, failure).ConfigureAwait(false);
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
                        LogScopeDisposedRollbackFaulted(_logger, ex);
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
                    LogScopeDisposedRollbackFaulted(_logger, ex);
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

            // Unwind innermost-first: every still-active unit fails with ScopeDisposed.
            for (var i = _frames.Count - 1; i >= 0; i--)
            {
                var unit = _frames[i].Engine;
                var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.ScopeDisposed);

                if (unit.TryClaimFailed(failure, out var claim))
                {
                    drained.Add((unit, claim, failure));
                    LogScopeDisposedLeak(_logger);
                }
            }

            _frames.Clear();
            Current = null;
        }

        return drained;
    }

    private static async ValueTask _DisposeUnitAsync(
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
            var frame = _frames.FirstOrDefault(f => ReferenceEquals(f.Engine, root));

            if (frame is not null)
            {
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

        _PopFrame(root);

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
            var index = _frames.FindIndex(f => ReferenceEquals(f.Engine, unit));

            if (index < 0)
            {
                return;
            }

            _frames.RemoveAt(index);
            Current = _frames.Count > 0 ? _frames[^1].Handle : null;
        }
    }

    private void _RunBackground(Func<Task> work)
    {
        BackgroundFault.Observe(
            Task.Run(work),
            _logger,
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
            LogBackgroundDrainFaulted(_logger, ex);
        }
    }

    private void _WarnForgottenCompletion(IUnitOfWorkResource? resource)
    {
        // Only observed mode: the unit was neither completed nor rolled back (this is the abandon path) and
        // the caller's transaction already finished, so the after-commit work was silently discarded.
        if (resource is { IsOwned: false, IsTransactionCompleted: true })
        {
            LogForgottenCompletion(_logger);
        }
    }

    private void _ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

    private sealed class Frame(Internal.UnitOfWork engine, UnitOfWorkHandle handle)
    {
        public Internal.UnitOfWork Engine { get; } = engine;

        public UnitOfWorkHandle Handle { get; } = handle;

        public int ActiveChildren { get; set; }
    }

    private sealed class NoOpAdoption : IDisposable
    {
        public static readonly NoOpAdoption Instance = new();

        public void Dispose() { }
    }

    private sealed class Adoption(UnitOfWorkManager manager, IUnitOfWork adopted) : IDisposable
    {
        public void Dispose()
        {
            lock (manager._gate)
            {
                if (ReferenceEquals(manager.Current, adopted))
                {
                    manager.Current = null;
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
}
