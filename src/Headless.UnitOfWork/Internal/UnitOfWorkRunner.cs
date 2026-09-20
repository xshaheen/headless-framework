// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The one body behind every raw-ADO <c>RunAsync</c> helper: begin an owned unit of work through the factory's
/// provider primitive, run the operation, then complete it (commit, then drain). The providers supply only how
/// their resource begins.
/// </summary>
/// <remarks>
/// An operation fault rolls the unit back and propagates the ORIGINAL exception; a rollback fault is logged and
/// never replaces it. A commit fault propagates as-is (the unit is already <see cref="UnitOfWorkState.Failed" />).
/// A drain fault after a durable commit — <see cref="IUnitOfWork.CompleteAsync" /> throwing while the unit is
/// already <see cref="UnitOfWorkState.Completed" /> — is logged and the operation's result is returned: surfacing
/// it would invite a retry that double-applies a committed transaction, and the enlisted durable rows are
/// relay-recoverable.
/// </remarks>
internal static partial class UnitOfWorkRunner
{
    /// <summary>
    /// The factory's logger keeps runner faults in the unit-of-work category; a foreign factory implementation
    /// has no logger to share, so the runner stays silent rather than guessing a category.
    /// </summary>
    public static ILogger LoggerFor(IUnitOfWorkFactory factory)
    {
        return factory is UnitOfWorkFactory owned ? owned.Logger : NullLogger.Instance;
    }

    public static async Task<TResult> RunAsync<TResult>(
        IUnitOfWorkFactory factory,
        Func<CancellationToken, ValueTask<IUnitOfWorkResource>> beginResource,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var unitOfWork = await factory
            .BeginAsync(beginResource, options: null, cancellationToken)
            .ConfigureAwait(false);

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
