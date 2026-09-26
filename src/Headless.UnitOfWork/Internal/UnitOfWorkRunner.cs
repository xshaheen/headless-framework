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
/// An owned attempt that faults before its commit starts is unwound first, so a replay never meets a still-open
/// transaction, and the ORIGINAL fault reaches the strategy, which may replay the whole block with a fresh unit.
/// A fault that must not replay never reaches the strategy: once the commit has started (the transaction may
/// already be durable) or after <see cref="IUnitOfWork.PreventRetry" />, the fault is captured and rethrown after
/// the strategy returns. A drain fault after a durable commit — <see cref="IUnitOfWork.CompleteAsync" /> throwing
/// while the unit is already <see cref="UnitOfWorkState.Completed" /> — is logged and the block's result is
/// returned: surfacing it would invite a retry that double-applies a committed transaction, and the enlisted
/// durable rows are relay-recoverable. An unwind fault is logged and never replaces the attempt's own fault.
/// </para>
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

    /// <summary>
    /// Joins <paramref name="joined" /> when it is not <see langword="null" />; otherwise runs the owned block
    /// inside <paramref name="strategy" /> under the replay and refusal policy described on the type.
    /// </summary>
    /// <param name="joined">The live unit already bound to the resource, or <see langword="null" /> to begin one.</param>
    /// <param name="begin">Begins a fresh owned unit for one attempt and binds it to the resource.</param>
    /// <param name="operation">The caller's block.</param>
    /// <param name="strategy">The provider's replay loop; <see cref="NoReplayUnitOfWorkExecutionStrategy" /> runs once.</param>
    /// <param name="unwind">How an attempt that faulted before its commit is unwound.</param>
    /// <param name="logger">Receives unwind and post-commit drain faults.</param>
    /// <param name="cancellationToken">Forwarded to the strategy, which hands it to each attempt.</param>
    public static async Task<TResult> RunAsync<TResult>(
        IUnitOfWork? joined,
        Func<CancellationToken, ValueTask<IUnitOfWork>> begin,
        Func<IUnitOfWork, CancellationToken, Task<TResult>> operation,
        IUnitOfWorkExecutionStrategy strategy,
        UnitOfWorkAttemptUnwind unwind,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (joined is not null)
        {
            return await RunJoinedAsync(joined, operation, cancellationToken).ConfigureAwait(false);
        }

        var outcome = await strategy
            .ExecuteAsync(ct => _RunAttemptAsync(begin, operation, unwind, logger, ct), cancellationToken)
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
        UnitOfWorkAttemptUnwind unwind,
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
            if (unitOfWork is not null)
            {
                // A commit fault leaves the unit terminal, so this is a no-op there. Before the commit, the unwind
                // must finish before the strategy replays, or the replay's begin meets a still-open transaction on
                // the same resource.
                await _UnwindQuietlyAsync(unitOfWork, unwind, logger).ConfigureAwait(false);
            }

            // Once the commit has started (it may have committed before the fault) or the block marked itself
            // non-replayable, the fault must NOT reach the strategy's replay loop: capture it and rethrow it after
            // the strategy returns.
            if (commitStarted || unitOfWork?.IsRetryPrevented == true)
            {
                return new AttemptOutcome<TResult>(default!, ExceptionDispatchInfo.Capture(ex));
            }

            throw;
        }
    }

    private static async ValueTask _UnwindQuietlyAsync(
        IUnitOfWork unitOfWork,
        UnitOfWorkAttemptUnwind unwind,
        ILogger logger
    )
    {
        // The attempt's own fault is the caller's outcome; an unwind fault must not mask it, but it is still a
        // real secondary failure, so it is logged rather than dropped.
        if (unwind == UnitOfWorkAttemptUnwind.RollBack)
        {
            try
            {
                await unitOfWork.RollbackAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogRollbackFaulted(logger, ex);
            }

            return;
        }

        try
        {
            await unitOfWork.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAttemptDisposeFaulted(logger, ex);
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
        Level = LogLevel.Error,
        Message = "Disposing the unit of work of a faulted RunAsync attempt faulted as well; the attempt's own exception is what surfaces."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogAttemptDisposeFaulted(ILogger logger, Exception exception);
}

/// <summary>How <see cref="UnitOfWorkRunner" /> unwinds an attempt whose block faulted before the commit started.</summary>
/// <remarks>
/// Both roll the transaction back; they differ in the <see cref="UnitOfWorkFailureReason" /> the unit's
/// <c>OnFailed</c> callbacks receive. The raw-ADO helpers have always reported <see cref="UnitOfWorkFailureReason.RolledBack" />
/// and EF <see cref="UnitOfWorkFailureReason.Abandoned" />; each provider keeps its reported reason until the two are
/// deliberately aligned, because an <c>OnFailed</c> handler may branch on it.
/// </remarks>
internal enum UnitOfWorkAttemptUnwind
{
    /// <summary><see cref="IUnitOfWork.RollbackAsync" />: <c>OnFailed</c> receives <see cref="UnitOfWorkFailureReason.RolledBack" />.</summary>
    RollBack = 0,

    /// <summary><see cref="IAsyncDisposable.DisposeAsync" />: <c>OnFailed</c> receives <see cref="UnitOfWorkFailureReason.Abandoned" />.</summary>
    Dispose = 1,
}
