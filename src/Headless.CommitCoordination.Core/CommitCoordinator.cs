// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination;

/// <summary>
/// Default in-process implementation of <see cref="ICommitCoordinator" />.
/// </summary>
[PublicAPI]
public sealed partial class CommitCoordinator : ICommitCoordinator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, ICommitWorkBuffer> _buffers = [];
    private readonly Dictionary<Type, ICommitCapability> _capabilities;
    private readonly CommitCoordinator? _parent;
    private readonly ILogger _logger;
    private readonly ConcurrentBag<IDisposable> _promotedRegistrations = [];
    private List<CommitCallbackRegistration> _commitCallbacks = [];
    private List<CommitCallbackRegistration> _rollbackCallbacks = [];
    private int _state;

    /// <summary>
    /// Initializes a new root coordinator.
    /// </summary>
    /// <param name="capabilities">The immutable provider capabilities.</param>
    /// <param name="logger">The logger used to surface ignored racing terminal signals.</param>
    public CommitCoordinator(IEnumerable<ICommitCapability>? capabilities = null, ILogger? logger = null)
    {
        Root = this;
        _capabilities = _CreateCapabilityMap(capabilities);
        _logger = logger ?? NullLogger.Instance;
    }

    private CommitCoordinator(CommitCoordinator parent)
    {
        _parent = parent;
        Root = parent.Root;
        _capabilities = parent._capabilities;
        _logger = parent._logger;
    }

    internal CommitCoordinator Root { get; }

    /// <inheritdoc />
    public CommitCoordinatorState State => (CommitCoordinatorState)Volatile.Read(ref _state);

    internal CommitCoordinator CreateChild()
    {
        _ThrowIfNotActive();

        return new CommitCoordinator(this);
    }

    /// <inheritdoc />
    public IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work)
    {
        Argument.IsNotNull(work);
        _ThrowIfNotActive();

        if (_parent is not null)
        {
            var registration = Root.OnCommit(work);
            _promotedRegistrations.Add(registration);
            return registration;
        }

        return _AddCallback(work, _commitCallbacks);
    }

    /// <inheritdoc />
    public IDisposable OnRollback(Func<CommitContext, CancellationToken, ValueTask> work)
    {
        Argument.IsNotNull(work);
        _ThrowIfNotActive();

        if (_parent is not null)
        {
            var registration = Root.OnRollback(work);
            _promotedRegistrations.Add(registration);
            return registration;
        }

        return _AddCallback(work, _rollbackCallbacks);
    }

    /// <inheritdoc />
    public TBuffer GetOrAdd<TBuffer>(Func<ICommitCoordinator, TBuffer> factory)
        where TBuffer : class, ICommitWorkBuffer
    {
        Argument.IsNotNull(factory);

        return GetOrAdd<TBuffer, Func<ICommitCoordinator, TBuffer>>(factory, static (coordinator, create) =>
            create(coordinator)
        );
    }

    /// <inheritdoc />
    public TBuffer GetOrAdd<TBuffer, TState>(TState state, Func<ICommitCoordinator, TState, TBuffer> factory)
        where TBuffer : class, ICommitWorkBuffer
    {
        Argument.IsNotNull(factory);
        _ThrowIfNotActive();

        if (_parent is not null)
        {
            return Root.GetOrAdd(state, factory);
        }

        lock (_gate)
        {
            _ThrowIfNotActive();

            var type = typeof(TBuffer);

            if (_buffers.TryGetValue(type, out var existing))
            {
                return (TBuffer)existing;
            }

            var buffer = factory(this, state);
            _buffers.Add(type, buffer);

            return buffer;
        }
    }

    internal static void DrainInBackground(
        CommitTerminalClaim claim,
        IServiceProvider services,
        Action? afterDrain = null
    )
    {
        _ = Task.Run(() => _DrainThenAsync(claim, services, afterDrain))
            .ContinueWith(
                static (t, state) => LogBackgroundDrainFaulted((ILogger)state!, t.Exception),
                claim.Coordinator._logger,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
    }

    private static async Task _DrainThenAsync(CommitTerminalClaim claim, IServiceProvider services, Action? afterDrain)
    {
        try
        {
            await DrainAsync(claim, services, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // Runs after the drain completes (or faults) so a caller offloading the drain can order post-drain work
            // — e.g. disposing promoted child registrations only once the rollback drain has invoked them.
            afterDrain?.Invoke();
        }
    }

    /// <inheritdoc />
    public bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class, ICommitCapability
    {
        if (_capabilities.TryGetValue(typeof(TCapability), out var value) && value is TCapability typed)
        {
            capability = typed;
            return true;
        }

        capability = null;
        return false;
    }

    /// <summary>
    /// Signals a terminal outcome: claims terminal state synchronously, then runs the asynchronous drain. A
    /// convenience composite over <see cref="TryClaimTerminal" /> + <see cref="DrainAsync" /> for owners that both
    /// claim and drain on the same thread (the in-process driven path). Out-of-band sources call the two halves
    /// separately so the claim lands synchronously on the commit edge before the drain is scheduled.
    /// </summary>
    internal ValueTask SignalAsync(CommitOutcome outcome, IServiceProvider services, CancellationToken cancellationToken)
    {
        return TryClaimTerminal(outcome, out var claim)
            ? DrainAsync(claim, services, cancellationToken)
            : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Synchronously claims the terminal outcome and captures the work to drain. First-claim-wins: a later claim on
    /// an already-terminal coordinator is an ignored, logged no-op. This is intentionally synchronous so the claim
    /// settles on the caller's own thread (e.g. the commit edge) before any asynchronous drain is scheduled — a
    /// racing <see cref="ICommitScope" /> disposal can then observe the claim and never roll back committed work.
    /// </summary>
    /// <param name="outcome">The terminal outcome being claimed.</param>
    /// <param name="claim">When this returns <see langword="true" />, the captured drain to pass to <see cref="DrainAsync" />.</param>
    /// <returns><see langword="true" /> when this caller won the claim and must drain; otherwise <see langword="false" />.</returns>
    internal bool TryClaimTerminal(CommitOutcome outcome, out CommitTerminalClaim claim)
    {
        claim = default;

        if (_parent is not null)
        {
            if (outcome == CommitOutcome.RolledBack)
            {
                // A child rollback dooms the root; the root's drain (when this caller wins it) is what gets run.
                var rootWon = Root.TryClaimTerminal(CommitOutcome.RolledBack, out claim);
                _SetTerminal(CommitCoordinatorState.RolledBack);

                return rootWon;
            }

            // A child commit promotes its work to the root and has nothing of its own to drain.
            _SetTerminal(CommitCoordinatorState.Committed);

            return false;
        }

        var terminalState = outcome == CommitOutcome.Committed
            ? CommitCoordinatorState.Committed
            : CommitCoordinatorState.RolledBack;

        // The claim is the single state-transition authority: it moves Active -> terminal atomically so no other path
        // (not even the drain) writes _state. A drain fault therefore can never strand the coordinator mid-transition.
        if (Interlocked.CompareExchange(ref _state, (int)terminalState, (int)CommitCoordinatorState.Active) != (int)CommitCoordinatorState.Active)
        {
            // First terminal signal already won (D10): a second signal — e.g. EF TransactionCommitted racing the
            // SqlClient diagnostic, or a Dispose racing a Rollback — is an ignored no-op, surfaced so provider
            // double-signal bugs stay diagnosable instead of silent.
            LogIgnoredRacingSignal(_logger, State, outcome);

            return false;
        }

        List<CommitCallbackRegistration> callbacks;
        List<ICommitWorkBuffer> buffers;

        lock (_gate)
        {
            callbacks = outcome == CommitOutcome.Committed ? _commitCallbacks : _rollbackCallbacks;
            _commitCallbacks = [];
            _rollbackCallbacks = [];
            buffers = [.. _buffers.Values];
            _buffers.Clear();
        }

        claim = new CommitTerminalClaim(this, outcome, callbacks, buffers);

        return true;
    }

    /// <summary>
    /// Runs the asynchronous drain for a won claim: invokes the captured callbacks (fault-aggregating) and disposes
    /// the captured buffers. Never writes <c>_state</c> (the claim already settled it) and never touches the ambient
    /// scope — ambient-frame ownership belongs solely to <see cref="ICommitScope" /> disposal.
    /// </summary>
    /// <param name="claim">The claim won from <see cref="TryClaimTerminal" />.</param>
    /// <param name="services">The service provider for the drain's <see cref="CommitContext" />.</param>
    /// <param name="cancellationToken">Observed only after all work has drained.</param>
    /// <returns>The drain task.</returns>
    internal static async ValueTask DrainAsync(
        CommitTerminalClaim claim,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var context = new CommitContext(claim.Coordinator._capabilities)
        {
            Services = services,
            Outcome = claim.Outcome,
        };

        var exceptions = new List<Exception>();

        foreach (var registration in claim.Callbacks)
        {
            if (registration.IsDisposed)
            {
                continue;
            }

            try
            {
                await registration.Work(context, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        await _DisposeBuffersAsync(claim.Buffers, exceptions).ConfigureAwait(false);

        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(exceptions);
        }
    }

    internal void DisposePromotedRegistrations()
    {
        if (
            _parent is not null
            && (State == CommitCoordinatorState.Committed || Root.State != CommitCoordinatorState.Active)
        )
        {
            return;
        }

        foreach (var registration in _promotedRegistrations)
        {
            registration.Dispose();
        }
    }

    private IDisposable _AddCallback(
        Func<CommitContext, CancellationToken, ValueTask> work,
        List<CommitCallbackRegistration> target
    )
    {
        lock (_gate)
        {
            _ThrowIfNotActive();

            var registration = new CommitCallbackRegistration(work);
            target.Add(registration);

            return registration;
        }
    }

    private void _ThrowIfNotActive()
    {
        if (State != CommitCoordinatorState.Active)
        {
            throw new InvalidOperationException($"Commit scope already {State}.");
        }
    }

    private void _SetTerminal(CommitCoordinatorState terminalState)
    {
        var state = State;

        if (state is CommitCoordinatorState.Committed or CommitCoordinatorState.RolledBack)
        {
            LogIgnoredRacingSignal(
                _logger,
                state,
                terminalState == CommitCoordinatorState.Committed ? CommitOutcome.Committed : CommitOutcome.RolledBack
            );

            return;
        }

        Volatile.Write(ref _state, (int)terminalState);
    }

    private static Dictionary<Type, ICommitCapability> _CreateCapabilityMap(IEnumerable<ICommitCapability>? capabilities)
    {
        var map = new Dictionary<Type, ICommitCapability>();

        if (capabilities is null)
        {
            return map;
        }

        foreach (var capability in capabilities)
        {
            map[capability.GetType()] = capability;

            foreach (var contract in capability.GetType().GetInterfaces())
            {
                // Skip the bare ICommitCapability marker: indexing it as a key would make the last-registered
                // capability answer TryGetCapability<ICommitCapability>() — a meaningless lookup. Only concrete
                // capability contracts (the interfaces a consumer actually queries) belong in the map.
                if (contract != typeof(ICommitCapability) && typeof(ICommitCapability).IsAssignableFrom(contract))
                {
                    map[contract] = capability;
                }
            }
        }

        return map;
    }

    private static async ValueTask _DisposeBuffersAsync(
        List<ICommitWorkBuffer> buffers,
        List<Exception> exceptions
    )
    {
        foreach (var buffer in buffers)
        {
            try
            {
                switch (buffer)
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
    /// The work captured by a won terminal claim, carried from the synchronous <see cref="TryClaimTerminal" /> to the
    /// asynchronous <see cref="DrainAsync" />. Targets <see cref="Coordinator" />, which is the root for a child
    /// rollback claim.
    /// </summary>
    internal readonly struct CommitTerminalClaim(
        CommitCoordinator coordinator,
        CommitOutcome outcome,
        List<CommitCallbackRegistration> callbacks,
        List<ICommitWorkBuffer> buffers
    )
    {
        public CommitCoordinator Coordinator { get; } = coordinator;

        public CommitOutcome Outcome { get; } = outcome;

        public List<CommitCallbackRegistration> Callbacks { get; } = callbacks;

        public List<ICommitWorkBuffer> Buffers { get; } = buffers;
    }

    internal sealed class CommitCallbackRegistration(Func<CommitContext, CancellationToken, ValueTask> work)
        : IDisposable
    {
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public Func<CommitContext, CancellationToken, ValueTask> Work { get; } = work;

        public void Dispose()
        {
            Volatile.Write(ref _disposed, 1);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Commit scope already {State}; ignoring {Signal} signal."
    )]
    private static partial void LogIgnoredRacingSignal(
        ILogger logger,
        CommitCoordinatorState state,
        CommitOutcome signal
    );

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "A commit coordination background drain faulted."
    )]
    private static partial void LogBackgroundDrainFaulted(ILogger logger, Exception? exception);
}
