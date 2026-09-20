// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.UnitOfWork.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.UnitOfWork;

/// <summary>
/// The singleton unit-of-work factory: opens independent units and drives each one's commit, rollback, and
/// drain when its handle asks. Holds nothing about the units it opened — no slot, no current unit, no frames —
/// so it is safe to share across scopes and threads. Created by DI through <c>AddUnitOfWork()</c>; tests
/// construct it directly.
/// </summary>
/// <param name="logger">Logger for the forgotten-completion warning and background drain faults.</param>
/// <param name="services">
/// The root provider, which <see cref="IUnitOfWork.GetFeature{TFeature}" /> resolves features from;
/// <see langword="null" /> outside DI, where no feature resolves.
/// </param>
internal sealed partial class UnitOfWorkFactory(
    ILogger<UnitOfWorkFactory>? logger = null,
    IServiceProvider? services = null
) : IUnitOfWorkFactory
{
    /// <summary>The factory's logger, shared with the provider runners so post-commit drain faults land in one category.</summary>
    internal ILogger Logger { get; } = logger ?? NullLogger<UnitOfWorkFactory>.Instance;

    /// <inheritdoc />
    public ValueTask<IUnitOfWork> BeginAsync(
        UnitOfWorkOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
#pragma warning disable CA2000 // The handle is the value being handed out; the caller completes or disposes it.
        return ValueTask.FromResult(_Open(resource: null));
#pragma warning restore CA2000
    }

    /// <inheritdoc />
    public async ValueTask<IUnitOfWork> BeginAsync(
        Func<CancellationToken, ValueTask<IUnitOfWorkResource>> beginResource,
        UnitOfWorkOptions? options,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(beginResource);

        var resource = await beginResource(cancellationToken).ConfigureAwait(false);

        return _Open(resource);
    }

    /// <inheritdoc />
    public IUnitOfWork Enlist(IUnitOfWorkResource resource, UnitOfWorkOptions? options = null)
    {
        Argument.IsNotNull(resource);

        return _Open(resource);
    }

    private IUnitOfWork _Open(IUnitOfWorkResource? resource) =>
        new UnitOfWorkHandle(new Internal.UnitOfWork(resource, Logger), this);

    /// <summary>
    /// Resolves the feature <typeparamref name="TFeature" /> from the host's root provider. A plain service
    /// lookup: nothing is created or cached per unit, so every handle sees the same instance, and a factory
    /// constructed outside DI resolves nothing. A feature is therefore a singleton by contract; a scoped
    /// registration fails here under scope validation instead of silently resolving from the root.
    /// </summary>
    internal TFeature? GetFeature<TFeature>()
        where TFeature : class, IUnitOfWorkFeature => services?.GetService(typeof(TFeature)) as TFeature;

    /// <summary>Completes a unit: claim, commit (owned), drain.</summary>
    internal async ValueTask CompleteAsync(Internal.UnitOfWork unit, CancellationToken cancellationToken)
    {
        if (!unit.TryClaimCompleted(out var claim))
        {
            _ThrowForTerminalUnit(unit);
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

    /// <summary>Rolls a unit back explicitly: claim Failed(RolledBack), roll the owned resource back, drain.</summary>
    internal async ValueTask RollbackAsync(Internal.UnitOfWork unit)
    {
        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.RolledBack);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            return; // Idempotent: a terminal unit ignores the conflicting verb.
        }

        await _RollBackAndDrainAsync(unit, claim, failure, propagateFaults: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes a handle without a completion verb: the claim is synchronous; the rollback and the drain run
    /// observed in the background so neither a synchronous disposer nor an async one blocks on the drain's
    /// unit-state disposal (a captured SynchronizationContext could otherwise deadlock).
    /// </summary>
    internal void Dispose(Internal.UnitOfWork unit)
    {
        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.Abandoned);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            return; // A dispose after CompleteAsync or RollbackAsync is a no-op.
        }

        _WarnForgottenCompletion(unit.Resource);
        _RunBackground(() => _RollBackAndDrainAsync(unit, claim, failure, propagateFaults: false).AsTask());
    }

    /// <summary>
    /// Disposes a handle asynchronously without a completion verb. Faults are logged, never thrown: an
    /// <c>await using</c> usually disposes while the caller unwinds its own exception, which a dispose fault
    /// would silently replace.
    /// </summary>
    internal async ValueTask DisposeAsync(Internal.UnitOfWork unit)
    {
        var failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.Abandoned);

        if (!unit.TryClaimFailed(failure, out var claim))
        {
            return;
        }

        _WarnForgottenCompletion(unit.Resource);
        await _RollBackAndDrainAsync(unit, claim, failure, propagateFaults: false).ConfigureAwait(false);
    }

    /// <summary>
    /// The failure path shared by rollback and dispose: roll the owned resource back, then drain. The drain
    /// always runs — a rollback fault must not skip the <c>OnFailed</c> callbacks or the unit-state disposal the
    /// abandon contract promises. <paramref name="propagateFaults" /> is true only for the explicit
    /// <c>RollbackAsync</c> verb, whose caller asked for the outcome; an implicit dispose logs instead, because it
    /// usually runs while the caller unwinds its own exception, which a thrown fault would silently replace.
    /// </summary>
    private async ValueTask _RollBackAndDrainAsync(
        Internal.UnitOfWork unit,
        Internal.UnitOfWork.UnitOfWorkTerminalClaim claim,
        UnitOfWorkFailure failure,
        bool propagateFaults
    )
    {
        ExceptionDispatchInfo? rollbackFault = null;

        if (unit.Resource is { IsOwned: true } resource)
        {
            try
            {
                await resource.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (propagateFaults)
            {
                rollbackFault = ExceptionDispatchInfo.Capture(ex);
            }
            catch (Exception ex)
            {
                LogAbandonRollbackFaulted(Logger, ex);
            }
        }

        if (propagateFaults && rollbackFault is null)
        {
            await Internal.UnitOfWork.DrainFailedAsync(claim, failure).ConfigureAwait(false);

            return;
        }

        // The rollback fault (or the implicit dispose) is the outcome; a drain fault must not replace it.
        await _DrainFailedQuietlyAsync(claim, failure).ConfigureAwait(false);
        rollbackFault?.Throw();
    }

    private void _RunBackground(Func<Task> work)
    {
        BackgroundFault.Observe(
            Task.Run(work),
            Logger,
            static (logger, exception) => LogBackgroundDrainFaulted(logger, exception.InnerException)
        );
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
            // A failure-drain fault (a unit-state disposal fault) must not mask the caller's own exception.
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

        throw new InvalidOperationException(
            $"The unit of work has already failed ({unit.Failure?.Reason}) and cannot be completed. Begin a new unit of work."
        );
    }

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "A unit of work enlisted with Enlist(...) was disposed without CompleteAsync or RollbackAsync after its transaction completed; the after-commit work was discarded and durable rows will be recovered by the relay."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogForgottenCompletion(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "A unit-of-work failure drain faulted (a unit-state disposal threw); logged so it cannot mask the unit's own outcome."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogBackgroundDrainFaulted(ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Rolling back a unit of work whose commit faulted failed as well; its transaction may still be open."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogCommitFaultRollbackFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Rolling back an abandoned unit of work faulted; its OnFailed callbacks and unit state were still drained, and its transaction may still be open."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogAbandonRollbackFaulted(ILogger logger, Exception exception);
}
