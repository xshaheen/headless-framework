// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.Retry;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The one body behind every <c>RunAsync</c> helper, EF and raw ADO alike: join the unit already bound to the
/// resource when there is one, otherwise run begin → block → complete as one attempt inside the provider's
/// <see cref="IUnitOfWorkExecutionStrategy" />. The providers supply only the binding lookup, how their unit
/// begins, and the strategy; the replay and refusal policy lives here once, so the spellings cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// A joined block runs inside the owner's unit, outside any strategy of its own, and neither commits nor rolls
/// back: a fault propagates to the owner's block, which is what unwinds (and, under a replaying owner, replays)
/// the unit, and a joined block that ends the unit itself is refused once it returns.
/// </para>
/// <para>
/// An owned attempt that faults before its commit starts is rolled back first, so a replay never meets a
/// still-open transaction, and the ORIGINAL fault reaches the strategy, which may replay the whole block with a
/// fresh unit. The rollback is explicit on every provider, so <c>OnFailed</c> receives
/// <see cref="UnitOfWorkFailureReason.RolledBack" /> whatever spelling ran the block. A fault that must not
/// replay never reaches the strategy: once the commit has started (the transaction may already be durable),
/// after <see cref="IUnitOfWork.PreventRetry" />, or after the block ended the unit by its own hand (its
/// committed work must not be applied twice), the fault is captured and rethrown after the strategy returns. A
/// drain fault after a durable commit — <see cref="IUnitOfWork.CompleteAsync" /> throwing while the unit is
/// already <see cref="UnitOfWorkState.Completed" /> — is logged and the block's result is returned: surfacing it
/// would invite a retry that double-applies a committed transaction, and the enlisted durable rows are
/// relay-recoverable. A rollback fault is logged and never replaces the attempt's own fault.
/// </para>
/// </remarks>
internal static partial class UnitOfWorkRunner
{
    public const string JoinedBlockEndedUnitMessage =
        "A block that joined a unit of work through RunAsync completed, rolled back, or disposed it. The owner of the unit decides its outcome: return normally to keep it open, or throw to fault it. If the joined code must decide the outcome itself, hand it the unit's owner instead of the unit.";

    public const string OwnedBlockEndedUnitMessage =
        "A block that owns its unit of work through RunAsync rolled it back or disposed it and then returned a result. RunAsync completes the unit when the block returns and rolls it back when the block throws; throw to fail the block instead of ending the unit yourself.";

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

    /// <summary>
    /// Joins the unit <paramref name="findJoined" /> returns when it returns one; otherwise runs the owned block
    /// inside <paramref name="strategy" /> under the replay and refusal policy described on the type.
    /// </summary>
    /// <param name="findJoined">
    /// The provider's binding lookup: the live unit already bound to the resource, or <see langword="null" /> to
    /// begin one. Awaited before anything else so a stale unit the lookup abandons has released its transaction and
    /// connection before the begin runs; a provider that must refuse a begin on this resource throws from here, so
    /// the refusal surfaces before the strategy and never replays.
    /// </param>
    /// <param name="begin">Begins a fresh owned unit for one attempt and binds it to the resource.</param>
    /// <param name="operation">The caller's block.</param>
    /// <param name="strategy">The provider's replay loop; <see cref="NoReplayUnitOfWorkExecutionStrategy" /> runs once.</param>
    /// <param name="logger">Receives rollback and post-commit drain faults.</param>
    /// <param name="cancellationToken">Forwarded to the strategy, which hands it to each attempt.</param>
    public static async Task<TResult> RunAsync<TResult>(
        Func<ValueTask<IUnitOfWork?>> findJoined,
        Func<CancellationToken, ValueTask<IUnitOfWork>> begin,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        IUnitOfWorkExecutionStrategy strategy,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (await findJoined().ConfigureAwait(false) is { } joined)
        {
            return await RunJoinedAsync(joined, operation, cancellationToken).ConfigureAwait(false);
        }

        var outcome = await strategy
            .ExecuteAsync(ct => _RunAttemptAsync(begin, operation, logger, ct), cancellationToken)
            .ConfigureAwait(false);

        outcome.Error?.Throw();

        return outcome.Result;
    }

    /// <summary>
    /// Runs the owned block with a connection of its own per attempt: each attempt opens a connection, begins a
    /// unit on it, runs the block with both, completes, and disposes the connection once the attempt's unit is
    /// unwound. A replay therefore never reuses a connection a failed attempt left behind, and nothing can join
    /// the attempt's unit from outside because the caller never holds its connection.
    /// </summary>
    /// <param name="factory">The factory whose host default applies when <paramref name="retry" /> is <see langword="null" />.</param>
    /// <param name="openConnection">Returns a new, open connection the attempt owns.</param>
    /// <param name="begin">Begins an owned unit on the attempt's connection and binds it there.</param>
    /// <param name="operation">The caller's block.</param>
    /// <param name="retry">The call's replay policy, overriding the host default.</param>
    /// <param name="cancellationToken">Forwarded to the strategy, which hands it to each attempt.</param>
    public static async Task<TResult> RunPerAttemptConnectionAsync<TConnection, TResult>(
        IUnitOfWorkFactory factory,
        Func<CancellationToken, ValueTask<TConnection>> openConnection,
        Func<TConnection, CancellationToken, ValueTask<IUnitOfWork>> begin,
        Func<IUnitOfWork, TConnection, CancellationToken, Task<TResult>> operation,
        RetryStrategyOptions? retry,
        CancellationToken cancellationToken
    )
        where TConnection : class, IAsyncDisposable
    {
        var strategy =
            retry is not null ? ResiliencePipelineUnitOfWorkExecutionStrategy.Create(retry)
            : factory is UnitOfWorkFactory owned ? owned.DefaultReplayStrategy
            : NoReplayUnitOfWorkExecutionStrategy.Instance;
        var logger = LoggerFor(factory);

        var outcome = await strategy
            .ExecuteAsync(
                async ct =>
                {
                    var connection = await openConnection(ct).ConfigureAwait(false);

                    // Disposed only after the attempt returns, which is after its unit was unwound or completed.
                    await using (connection.ConfigureAwait(false))
                    {
                        return await _RunAttemptAsync(
                                attemptCt => begin(connection, attemptCt),
                                (unitOfWork, attemptCt) => operation(unitOfWork, connection, attemptCt),
                                UnitOfWorkAttemptUnwind.RollBack,
                                logger,
                                ct
                            )
                            .ConfigureAwait(false);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        outcome.Error?.Throw();

        return outcome.Result;
    }

    private static async Task<AttemptOutcome<TResult>> _RunAttemptAsync<TResult>(
        Func<CancellationToken, ValueTask<IUnitOfWork>> begin,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        IUnitOfWork? unitOfWork = null;
        var commitStarted = false;

        try
        {
            unitOfWork = await begin(cancellationToken).ConfigureAwait(false);

            var result = await operation(unitOfWork, cancellationToken).ConfigureAwait(false);

            if (unitOfWork.State != UnitOfWorkState.Active)
            {
                return _EndedByBlock(unitOfWork, result, logger);
            }

            commitStarted = true;

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

            return new AttemptOutcome<TResult>(result, Error: null);
        }
        catch (Exception ex)
        {
            // Read before the rollback below makes the unit terminal: a block that ended its own unit and then
            // threw may have committed, and a replay would apply that work twice.
            var endedByBlock = !commitStarted && unitOfWork is { State: not UnitOfWorkState.Active };

            if (unitOfWork is not null)
            {
                // A no-op on a terminal unit (a commit fault, or a unit the block ended). Before the commit, the
                // rollback must finish before the strategy replays, or the replay's begin meets a still-open
                // transaction on the same resource.
                await _RollBackQuietlyAsync(unitOfWork, logger).ConfigureAwait(false);
            }

            // Once the commit has started (it may have committed before the fault), the block ended the unit, or
            // the block marked itself non-replayable, the fault must NOT reach the strategy's replay loop: capture
            // it and rethrow it after the strategy returns.
            if (commitStarted || endedByBlock || unitOfWork?.IsRetryPrevented == true)
            {
                return new AttemptOutcome<TResult>(default!, ExceptionDispatchInfo.Capture(ex));
            }

            throw;
        }
    }

    /// <summary>
    /// The block returned normally after ending its own unit. A unit it completed holds durable work, so the
    /// result is returned and the misuse is logged rather than thrown: an exception here would invite a retry of
    /// committed work. A unit it rolled back or disposed has no result to stand behind, so that is refused, and
    /// never replayed, because the block's own verb decided the outcome.
    /// </summary>
    private static AttemptOutcome<TResult> _EndedByBlock<TResult>(
        IUnitOfWork unitOfWork,
        TResult result,
        ILogger logger
    )
    {
        if (unitOfWork.State == UnitOfWorkState.Completed)
        {
            LogBlockCompletedOwnUnit(logger);

            return new AttemptOutcome<TResult>(result, Error: null);
        }

        return new AttemptOutcome<TResult>(
            default!,
            ExceptionDispatchInfo.Capture(new InvalidOperationException(OwnedBlockEndedUnitMessage))
        );
    }

    private static async ValueTask _RollBackQuietlyAsync(IUnitOfWork unitOfWork, ILogger logger)
    {
        // The attempt's own fault is the caller's outcome; a rollback fault must not mask it, but it is still a
        // real secondary failure, so it is logged rather than dropped.
        try
        {
            await unitOfWork.RollbackAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogRollbackFaulted(logger, ex);
        }
    }

    private readonly record struct AttemptOutcome<TResult>(TResult Result, ExceptionDispatchInfo? Error);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        Message = "Rolling back a unit of work faulted after its operation threw; the enlisted work is discarded and the operation's own exception is rethrown."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogRollbackFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Error,
        Message = "Post-commit drain faulted after a durable commit; the relay will recover any enlisted work."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogPostCommitDrainFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "A RunAsync block completed its own unit of work before returning; RunAsync completes the unit when the block returns, so the block's own CompleteAsync is redundant and its result is returned as committed."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogBlockCompletedOwnUnit(ILogger logger);
}
