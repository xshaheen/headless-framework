// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The one body behind every raw-ADO <c>RunAsync</c> helper: join the unit already bound to the connection when
/// there is one, otherwise begin an owned unit of work through the provider's begin, run the operation, then
/// complete it (commit, then drain). The providers supply only the binding lookup and how their unit begins.
/// </summary>
/// <remarks>
/// A joined block runs inside the owner's unit and neither commits nor rolls back: a fault propagates to the
/// owner's block, which is what unwinds the unit, and a joined block that ends the unit itself is refused once it
/// returns. An owned operation fault rolls the unit back and propagates
/// the ORIGINAL exception; a rollback fault is logged and never replaces it. A commit fault propagates as-is (the unit is already <see cref="UnitOfWorkState.Failed" />).
/// A drain fault after a durable commit — <see cref="IUnitOfWork.CompleteAsync" /> throwing while the unit is
/// already <see cref="UnitOfWorkState.Completed" /> — is logged and the operation's result is returned: surfacing
/// it would invite a retry that double-applies a committed transaction, and the enlisted durable rows are
/// relay-recoverable.
/// </remarks>
internal static partial class UnitOfWorkRunner
{
    public const string JoinedBlockEndedUnitMessage =
        "A block that joined a unit of work through RunAsync completed, rolled back, or disposed it. The owner of the unit decides its outcome: return normally to keep it open, or throw to fault it. If the joined code must decide the outcome itself, hand it the unit's owner instead of the unit.";

    /// <summary>
    /// The factory's logger keeps runner faults in the unit-of-work category; a foreign factory implementation
    /// has no logger to share, so the runner stays silent rather than guessing a category.
    /// </summary>
    public static ILogger LoggerFor(IUnitOfWorkFactory factory)
    {
        return factory is UnitOfWorkFactory owned ? owned.Logger : NullLogger.Instance;
    }

    /// <summary>
    /// Runs a joined block on the owner's handle and refuses, after it returns, a block that ended the unit: the
    /// owner's own completion would otherwise surface as a swallowed "already completed" drain fault and report
    /// success for writes that ran outside the transaction.
    /// </summary>
    public static async Task<TResult> RunJoinedAsync<TResult>(
        IUnitOfWork joined,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var result = await operation(joined, cancellationToken).ConfigureAwait(false);

        if (joined.State != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException(JoinedBlockEndedUnitMessage);
        }

        return result;
    }

    public static async Task<TResult> RunAsync<TResult>(
        IUnitOfWork? joined,
        Func<CancellationToken, ValueTask<IUnitOfWork>> begin,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (joined is not null)
        {
            return await RunJoinedAsync(joined, operation, cancellationToken).ConfigureAwait(false);
        }

        var unitOfWork = await begin(cancellationToken).ConfigureAwait(false);

        await using (unitOfWork.ConfigureAwait(false))
        {
            TResult result;

            try
            {
                result = await operation(unitOfWork, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The explicit rollback discards the enlisted work now and keeps the factory's forgotten-completion
                // warning for hand-rolled enlistments only. Scope-local state is still disposed on rollback, and a
                // fault from that disposal must never replace the caller's real failure.
                try
                {
                    await unitOfWork.RollbackAsync().ConfigureAwait(false);
                }
                catch (Exception rollbackFault)
                {
                    LogRollbackFaulted(logger, rollbackFault);
                }

                throw;
            }

            try
            {
                await unitOfWork.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unitOfWork.State == UnitOfWorkState.Completed)
            {
                // The transaction is ALREADY durably committed; only the drain faulted. The drain is the dispatch
                // accelerator, so log and return the committed result instead of surfacing a phantom failure.
                LogPostCommitDrainFaulted(logger, ex);
            }

            return result;
        }
    }

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "Rolling back a unit of work faulted after its operation threw; the enlisted work is discarded and the operation's own exception is rethrown."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogRollbackFaulted(ILogger logger, Exception exception);

    /// <summary>Shared with the EF <c>RunAsync</c>, which applies the same post-commit drain policy.</summary>
    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Error,
        Message = "Post-commit drain faulted after a durable commit; the relay will recover any enlisted work."
    )]
    internal static partial void LogPostCommitDrainFaulted(ILogger logger, Exception exception);

    /// <summary>Used by the EF <c>RunAsync</c> when unwinding a replayed or non-replayable attempt's unit.</summary>
    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Error,
        Message = "Disposing the unit of work of a faulted RunAsync attempt faulted as well; the attempt's own exception is what surfaces."
    )]
    internal static partial void LogAttemptDisposeFaulted(ILogger logger, Exception exception);
}
