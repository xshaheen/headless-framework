// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>
/// The transactional inbox protocol shared by every relational storage: begin, enlist, run the handler, complete the
/// inbox row fenced on the attempt, commit, and resolve an ambiguous commit by probing the row. Providers supply only
/// how an attempt's transaction begins and, optionally, an ambient-transaction guard and a retry wrapper that may
/// replay an attempt that failed before the handler ran.
/// </summary>
internal abstract class InboxTransactionRunner(
    IDeliveryCoordinationResolver coordinationResolver,
    ITransactionalInboxStorage storage,
    ILogger logger
) : IInboxTransactionRunner
{
    public async Task ExecuteAsync(
        MediumMessage message,
        Func<IUnitOfWork, CancellationToken, Task> handler,
        CancellationToken cancellationToken
    )
    {
        EnsureNoAmbientTransaction();

        var attemptError = await ExecuteWithPreEntryRetryAsync(
                ct => _RunAttemptAsync(message, handler, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
        attemptError?.Throw();
    }

    /// <summary>Begins the attempt's transaction and enlists an observed-mode unit of work over it.</summary>
    protected abstract ValueTask<InboxAttemptTransaction> BeginAttemptAsync(CancellationToken cancellationToken);

    /// <summary>Throws when the attempt would nest inside a transaction its resources already carry.</summary>
    protected virtual void EnsureNoAmbientTransaction() { }

    /// <summary>
    /// Runs <paramref name="attempt" />, optionally replaying it on a transient fault. Only a fault before the handler
    /// ran can reach the wrapper as an exception; every later fault comes back as the attempt's result, so a replay
    /// never re-enters the handler inside the reserved attempt.
    /// </summary>
    protected virtual Task<ExceptionDispatchInfo?> ExecuteWithPreEntryRetryAsync(
        Func<CancellationToken, Task<ExceptionDispatchInfo?>> attempt,
        CancellationToken cancellationToken
    )
    {
        return attempt(cancellationToken);
    }

    private async Task<ExceptionDispatchInfo?> _RunAttemptAsync(
        MediumMessage message,
        Func<IUnitOfWork, CancellationToken, Task> handler,
        CancellationToken cancellationToken
    )
    {
        var handlerEntered = false;
        try
        {
            await using var attempt = await BeginAttemptAsync(cancellationToken).ConfigureAwait(false);
            var unitOfWork = attempt.UnitOfWork;
            var coordination = coordinationResolver.Resolve(unitOfWork);
            if (coordination.Status is not DeliveryCoordinationStatus.Compatible)
            {
                throw new InvalidOperationException(
                    $"The inbox transaction is incompatible with messaging storage: {coordination.Mismatch}."
                );
            }

            handlerEntered = true;
            await handler(unitOfWork, cancellationToken).ConfigureAwait(false);
            await attempt.FlushAsync(cancellationToken).ConfigureAwait(false);
            var completed = await storage
                .CompleteReceivedInboxAsync(message, attempt.Transaction, cancellationToken)
                .ConfigureAwait(false);
            if (!completed)
            {
                throw new StaleInboxAttemptException(message.StorageId);
            }

            try
            {
                await attempt.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception commitException)
            {
                if (
                    await storage.ProbeReceivedInboxCommitAsync(message, CancellationToken.None).ConfigureAwait(false)
                    is InboxCommitProbe.Committed
                )
                {
                    await _CompleteAfterDurableCommitAsync(unitOfWork, message).ConfigureAwait(false);
                    return null;
                }

                try
                {
                    await attempt.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    await unitOfWork.RollbackAsync().ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    if (
                        await storage
                            .ProbeReceivedInboxCommitAsync(message, CancellationToken.None)
                            .ConfigureAwait(false) is InboxCommitProbe.Committed
                    )
                    {
                        await _CompleteAfterDurableCommitAsync(unitOfWork, message).ConfigureAwait(false);
                        return null;
                    }

                    return ExceptionDispatchInfo.Capture(
                        new IndeterminateInboxCommitException(message.StorageId, commitException, rollbackException)
                    );
                }

                return ExceptionDispatchInfo.Capture(
                    new UncommittedInboxCommitException(message.StorageId, commitException)
                );
            }

            // Outside the commit's catch: a drain fault here is not a commit fault and must not be probed and
            // "completed" a second time.
            await _CompleteAfterDurableCommitAsync(unitOfWork, message).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (handlerEntered)
        {
            // Persisted inbox recovery owns every retry after entry, including failures raised while disposing the
            // unit of work or transaction.
            return ExceptionDispatchInfo.Capture(exception);
        }
    }

    // The row is durable once the commit (or its probe) says so; the unit's drain only accelerates the dispatch of
    // the enlisted callbacks. A drain fault must not turn a committed attempt into a retry that re-invokes the
    // consumer, so it is logged and the attempt reports success — the same policy as the unit-of-work runners; the
    // relay recovers any enlisted rows.
    private async Task _CompleteAfterDurableCommitAsync(IUnitOfWork unitOfWork, MediumMessage message)
    {
        try
        {
            await unitOfWork.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (unitOfWork.State == UnitOfWorkState.Completed)
        {
            logger.InboxPostCommitDrainFaulted(ex, message.StorageId);
        }
    }
}
