// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination;

/// <summary>
/// Default in-process implementation of <see cref="ICommitCoordinator" />. Internal: created only by
/// <see cref="CommitScopeFactory" /> and accessed by provider packages / tests via <c>InternalsVisibleTo</c>;
/// the public contract is <see cref="ICommitCoordinator" />.
/// </summary>
/// <param name="relational">The relational handle exposed as <see cref="Relational" />, if any.</param>
/// <param name="logger">The logger used to surface ignored conflicting signals and background drain faults.</param>
internal sealed partial class CommitCoordinator(IRelationalCommitContext? relational = null, ILogger? logger = null)
    : ICommitCoordinator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, object> _scopeState = [];
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private List<CommitCallbackRegistration> _commitCallbacks = [];
    private int _state;

    /// <inheritdoc />
    public CommitCoordinatorState State => (CommitCoordinatorState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public IRelationalCommitContext? Relational { get; } = relational;

    /// <inheritdoc />
    public IDisposable OnCommit(Func<ValueTask> work)
    {
        Argument.IsNotNull(work);

        lock (_gate)
        {
            _ThrowIfNotActive();

            var registration = new CommitCallbackRegistration(work);
            _commitCallbacks.Add(registration);

            return registration;
        }
    }

    /// <inheritdoc />
    public TState GetOrAdd<TState>(Func<ICommitCoordinator, TState> factory)
        where TState : class
    {
        Argument.IsNotNull(factory);

        return GetOrAdd<TState, Func<ICommitCoordinator, TState>>(
            factory,
            static (coordinator, create) => create(coordinator)
        );
    }

    /// <inheritdoc />
    public TState GetOrAdd<TState, TArg>(TArg arg, Func<ICommitCoordinator, TArg, TState> factory)
        where TState : class
    {
        Argument.IsNotNull(factory);

        // Exclusive lock by design — NOT a ConcurrentDictionary. The factory typically has a side effect (it
        // registers an OnCommit callback on construction), so the get-or-create must be atomic: a lock-free
        // double-create would register the callback twice and drain duplicate work. GetOrAdd is called once per
        // state type per transaction (first enlist), not per work item, so the contention is negligible.
        lock (_gate)
        {
            _ThrowIfNotActive();

            var type = typeof(TState);

            if (_scopeState.TryGetValue(type, out var existing))
            {
                return (TState)existing;
            }

            var state = factory(this, arg);
            _scopeState.Add(type, state);

            return state;
        }
    }

    /// <summary>
    /// Signals a terminal outcome: claims terminal state synchronously, then runs the asynchronous drain. A
    /// convenience composite over <see cref="TryClaimTerminal" /> + <see cref="DrainAsync" /> for owners that both
    /// claim and drain on the same thread. Out-of-band sources call the two halves separately so the claim lands
    /// synchronously on the commit edge before the drain is scheduled. A claimed drain always runs to completion,
    /// so there is no cancellation token: cancellation would risk abandoning already-committed work.
    /// </summary>
    internal ValueTask SignalAsync(CommitOutcome outcome)
    {
        return TryClaimTerminal(outcome, out var claim) ? DrainAsync(claim) : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Synchronously claims the terminal outcome for an explicit signal and captures the work to drain. First
    /// claim wins. A later signal is idempotent per outcome: the same outcome again is a silent no-op, while a
    /// conflicting outcome is ignored and logged so provider double-signal bugs stay diagnosable. The claim is
    /// intentionally synchronous so it settles on the caller's own thread (e.g. the commit edge) before any
    /// asynchronous drain is scheduled — a racing <see cref="ICommitScope" /> disposal then observes the claim and
    /// never rolls back committed work.
    /// </summary>
    /// <param name="outcome">The terminal outcome being claimed.</param>
    /// <param name="claim">When this returns <see langword="true" />, the captured drain to pass to <see cref="DrainAsync" />.</param>
    /// <returns><see langword="true" /> when this caller won the claim and must drain; otherwise <see langword="false" />.</returns>
    internal bool TryClaimTerminal(CommitOutcome outcome, out CommitTerminalClaim claim)
    {
        CommitOutcomeValidation.ThrowIfNotTerminal(outcome);

        if (_TryClaim(outcome, out claim))
        {
            return true;
        }

        var state = State;
        var claimedOutcome =
            state == CommitCoordinatorState.Committed ? CommitOutcome.Committed : CommitOutcome.RolledBack;

        if (claimedOutcome != outcome)
        {
            LogIgnoredConflictingSignal(_logger, state, outcome);
        }

        return false;
    }

    /// <summary>
    /// Claims rollback for an un-signalled disposal. Only an <see cref="CommitCoordinatorState.Active" />
    /// coordinator is claimed; an already-terminal one is left as-is without logging, because a dispose that
    /// follows a signal is the normal lifecycle, not a conflicting signal.
    /// </summary>
    /// <param name="claim">When this returns <see langword="true" />, the captured state to pass to <see cref="DrainAsync" />.</param>
    /// <returns><see langword="true" /> when this caller won the claim and must drain; otherwise <see langword="false" />.</returns>
    internal bool TryClaimAbandon(out CommitTerminalClaim claim)
    {
        return _TryClaim(CommitOutcome.RolledBack, out claim);
    }

    /// <summary>
    /// Runs the asynchronous drain for a won claim: on commit invokes the captured callbacks in registration order
    /// (fault-aggregating), then on both outcomes disposes the captured scope-local state. Never writes
    /// <c>_state</c> (the claim already settled it) and never touches the ambient scope — ambient-frame ownership
    /// belongs solely to <see cref="ICommitScope" /> disposal.
    /// </summary>
    /// <param name="claim">The claim won from <see cref="TryClaimTerminal" /> or <see cref="TryClaimAbandon" />.</param>
    /// <returns>The drain task.</returns>
    /// <remarks>
    /// There is intentionally no cancellation token: a claimed drain must run to completion — cancelling it would
    /// abandon already-committed work.
    /// </remarks>
    internal static async ValueTask DrainAsync(CommitTerminalClaim claim)
    {
        var exceptions = new List<Exception>();

        foreach (var registration in claim.Callbacks)
        {
            try
            {
                await registration.Work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        await _DisposeScopeStateAsync(claim.ScopeState, exceptions).ConfigureAwait(false);

        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(exceptions);
        }
    }

    /// <summary>
    /// Runs <see cref="DrainAsync" /> on the thread pool and logs a fault instead of surfacing it, for owners that
    /// cannot await the drain — a synchronous disposal, or a commit edge that must not block on callbacks.
    /// </summary>
    internal static void DrainInBackground(CommitTerminalClaim claim)
    {
        BackgroundFault.Observe(
            Task.Run(() => DrainAsync(claim).AsTask()),
            claim.Coordinator._logger,
            static (logger, exception) => LogBackgroundDrainFaulted(logger, exception)
        );
    }

    private bool _TryClaim(CommitOutcome outcome, out CommitTerminalClaim claim)
    {
        claim = default;

        var terminalState =
            outcome == CommitOutcome.Committed ? CommitCoordinatorState.Committed : CommitCoordinatorState.RolledBack;

        // The claim is the single state-transition authority: it moves Active -> terminal atomically so no other path
        // (not even the drain) writes _state. A drain fault therefore can never strand the coordinator mid-transition,
        // and a registration that acquires the gate after this point observes the terminal state and throws instead
        // of being silently stranded.
        if (
            Interlocked.CompareExchange(ref _state, (int)terminalState, (int)CommitCoordinatorState.Active)
            != (int)CommitCoordinatorState.Active
        )
        {
            return false;
        }

        List<CommitCallbackRegistration> callbacks;
        List<object> scopeState;

        lock (_gate)
        {
            // Deregistration is honored only up to the claim: the snapshot drops handles disposed while the
            // coordinator was active, and a handle disposed after this point is the documented no-op, so a callback
            // that won its place in the drain still runs.
            callbacks = outcome == CommitOutcome.Committed ? _commitCallbacks.FindAll(static r => !r.IsDisposed) : [];
            _commitCallbacks = [];
            // Most scopes never call GetOrAdd; skip the copy on the framework's most frequent path.
            scopeState = _scopeState.Count == 0 ? [] : [.. _scopeState.Values];
            _scopeState.Clear();
        }

        claim = new CommitTerminalClaim(this, outcome, callbacks, scopeState);

        return true;
    }

    private void _ThrowIfNotActive()
    {
        if (State != CommitCoordinatorState.Active)
        {
            throw new InvalidOperationException($"Commit scope already {State}.");
        }
    }

    private static async ValueTask _DisposeScopeStateAsync(List<object> scopeState, List<Exception> exceptions)
    {
        foreach (var state in scopeState)
        {
            try
            {
                switch (state)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;

                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
    }

    /// <summary>
    /// The work captured by a won terminal claim, carried from the synchronous claim to the asynchronous
    /// <see cref="DrainAsync" />. <see cref="Callbacks" /> is empty for a rollback claim.
    /// </summary>
    internal readonly struct CommitTerminalClaim(
        CommitCoordinator coordinator,
        CommitOutcome outcome,
        List<CommitCallbackRegistration> callbacks,
        List<object> scopeState
    )
    {
        public CommitCoordinator Coordinator { get; } = coordinator;

        public CommitOutcome Outcome { get; } = outcome;

        public List<CommitCallbackRegistration> Callbacks { get; } = callbacks;

        public List<object> ScopeState { get; } = scopeState;
    }

    internal sealed class CommitCallbackRegistration(Func<ValueTask> work) : IDisposable
    {
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public Func<ValueTask> Work { get; } = work;

        public void Dispose()
        {
            Volatile.Write(ref _disposed, 1);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Commit scope already {State}; ignoring conflicting {Signal} signal."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogIgnoredConflictingSignal(
        ILogger logger,
        CommitCoordinatorState state,
        CommitOutcome signal
    );

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "A commit coordination background drain faulted.")]
    // ReSharper disable once InconsistentNaming
    private static partial void LogBackgroundDrainFaulted(ILogger logger, Exception? exception);
}
