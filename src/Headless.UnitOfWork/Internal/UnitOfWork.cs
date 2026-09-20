// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The in-process unit-of-work engine: registration lists, scope-local state, the atomic terminal claim, the
/// ordered drains, fault aggregation, and deregistration handles. Ported from the former commit-coordination
/// coordinator with two additions: the <c>OnFailed</c> drain (log-and-continue) and child views that register
/// directly on the root engine so their registrations transfer with the root's completion. Internal: created
/// only by <see cref="UnitOfWorkManager" />; the public contract is the <see cref="Headless.UnitOfWork.IUnitOfWork" />
/// handle.
/// </summary>
/// <param name="resource">The resource this unit owns (or observes), if any.</param>
/// <param name="logger">The logger used to surface failure-callback faults and background drain faults.</param>
internal sealed partial class UnitOfWork(IUnitOfWorkResource? resource, ILogger? logger = null)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, object> _scopeState = [];
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private List<CompletedRegistration> _completedCallbacks = [];
    private List<FailedRegistration> _failedCallbacks = [];
    private int _state;
    private int _retryPrevented;

    /// <summary>The current lifecycle state; the terminal claim is the only writer.</summary>
    internal UnitOfWorkState State => (UnitOfWorkState)Volatile.Read(ref _state);

    /// <summary>The failure that terminated the unit, or <see langword="null" /> until then.</summary>
    internal UnitOfWorkFailure? Failure { get; private set; }

    /// <summary>The owned or observed resource, or <see langword="null" /> for a resource-less unit.</summary>
    internal IUnitOfWorkResource? Resource { get; } = resource;

    /// <summary>Whether <see cref="PreventRetry" /> was called.</summary>
    internal bool IsRetryPrevented => Volatile.Read(ref _retryPrevented) != 0;

    /// <summary>Marks the unit as not safely replayable by a retrying execution strategy; never resets.</summary>
    internal void PreventRetry() => Interlocked.Exchange(ref _retryPrevented, 1);

    /// <summary>Registers a completion callback. Child views call this and track the returned handle.</summary>
    internal IDisposable OnCompleted(Func<ValueTask> work)
    {
        Argument.IsNotNull(work);

        lock (_gate)
        {
            ThrowIfNotActive();

            var registration = new CompletedRegistration(work);
            _completedCallbacks.Add(registration);

            return registration;
        }
    }

    /// <summary>Registers a failure callback; faults during the failure drain are logged, never propagated.</summary>
    internal IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work)
    {
        Argument.IsNotNull(work);

        lock (_gate)
        {
            ThrowIfNotActive();

            var registration = new FailedRegistration(work);
            _failedCallbacks.Add(registration);

            return registration;
        }
    }

    /// <summary>See <see cref="Headless.UnitOfWork.IUnitOfWork.GetOrAdd{TState}" />.</summary>
    internal TState GetOrAdd<TState>(IUnitOfWork view, Func<IUnitOfWork, TState> factory)
        where TState : class
    {
        Argument.IsNotNull(factory);

        return GetOrAdd<TState, Func<IUnitOfWork, TState>>(
            view,
            factory,
            static (unitOfWork, create) => create(unitOfWork)
        );
    }

    /// <summary>See <see cref="Headless.UnitOfWork.IUnitOfWork.GetOrAdd{TState,TArg}" />.</summary>
    internal TState GetOrAdd<TState, TArg>(IUnitOfWork view, TArg arg, Func<IUnitOfWork, TArg, TState> factory)
        where TState : class
    {
        Argument.IsNotNull(factory);

        // Exclusive lock by design — NOT a ConcurrentDictionary. The factory typically has a side effect (it
        // registers an OnCompleted callback on construction), so the get-or-create must be atomic: a lock-free
        // double-create would register the callback twice and drain duplicate work.
        lock (_gate)
        {
            ThrowIfNotActive();

            var type = typeof(TState);

            if (_scopeState.TryGetValue(type, out var existing))
            {
                return (TState)existing;
            }

            var state = factory(view, arg);
            _scopeState.Add(type, state);

            return state;
        }
    }

    /// <summary>
    /// Synchronously claims the completed terminal state and captures the drain work without running it. The
    /// claim settles on the completer's thread before the asynchronous drain is scheduled, so a racing dispose
    /// observes <see cref="UnitOfWorkState.Completed" /> and never rolls committed work back. The caller
    /// drains via <see cref="DrainCompletedAsync" />.
    /// </summary>
    internal bool TryClaimCompleted(out UnitOfWorkTerminalClaim claim) =>
        _TryClaim(UnitOfWorkState.Completed, failure: null, out claim);

    /// <summary>
    /// Synchronously claims a failure terminal state and captures the drain work: rollback, abandon, scope
    /// dispose, commit fault, or a child abandon aborting the root.
    /// </summary>
    internal bool TryClaimFailed(UnitOfWorkFailure failure, out UnitOfWorkTerminalClaim claim) =>
        _TryClaim(UnitOfWorkState.Failed, failure, out claim);

    /// <summary>
    /// Moves an already-completed claim to <see cref="UnitOfWorkState.Failed" /> after the resource commit
    /// faulted, recording the failure. Only the completer calls this, on a claim it owns, so the transition
    /// always applies.
    /// </summary>
    internal void TransitionCompletedToFailed(UnitOfWorkFailure failure)
    {
        Interlocked.CompareExchange(ref _state, (int)UnitOfWorkState.Failed, (int)UnitOfWorkState.Completed);
        Failure = failure;
    }

    /// <summary>
    /// Runs the completion drain: the captured callbacks in registration order (fault-aggregating), then the
    /// disposal of the captured scope-local state. Never writes <c>_state</c>: a callback fault surfaces after
    /// the drain but leaves the unit <see cref="UnitOfWorkState.Completed" /> — the data is durable, and the
    /// exception must not be mistakable for a rollback.
    /// </summary>
    internal static async ValueTask DrainCompletedAsync(UnitOfWorkTerminalClaim claim)
    {
        var exceptions = new List<Exception>();

        foreach (var registration in claim.CompletedCallbacks)
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

        _ThrowIfAny(exceptions);
    }

    /// <summary>
    /// Runs the failure drain: the captured <c>OnFailed</c> callbacks in registration order — each fault is
    /// logged and the drain continues — then the disposal of the captured scope-local state (whose faults
    /// surface to the caller).
    /// </summary>
    internal static async ValueTask DrainFailedAsync(UnitOfWorkTerminalClaim claim, UnitOfWorkFailure failure)
    {
        foreach (var registration in claim.FailedCallbacks)
        {
            try
            {
                await registration.Work(failure).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogFailureCallbackFaulted(claim.Unit._logger, ex.Message, ex);
            }
        }

        var exceptions = new List<Exception>();

        await _DisposeScopeStateAsync(claim.ScopeState, exceptions).ConfigureAwait(false);

        _ThrowIfAny(exceptions);
    }

    private bool _TryClaim(UnitOfWorkState terminalState, UnitOfWorkFailure? failure, out UnitOfWorkTerminalClaim claim)
    {
        claim = default;

        // The claim is the single state-transition authority: it moves Active -> terminal atomically so no
        // other path (not even the drain) writes _state. A drain fault therefore can never strand the unit
        // mid-transition, and a registration that acquires the gate after this point observes the terminal
        // state and throws instead of being silently stranded.
        if (
            Interlocked.CompareExchange(ref _state, (int)terminalState, (int)UnitOfWorkState.Active)
            != (int)UnitOfWorkState.Active
        )
        {
            return false;
        }

        List<CompletedRegistration> completedCallbacks;
        List<FailedRegistration> failedCallbacks;
        List<object> scopeState;

        lock (_gate)
        {
            // Deregistration is honored only up to the claim: the snapshot drops handles disposed while the
            // unit was active, and a handle disposed after this point is the documented no-op, so a callback
            // that won its place in the drain still runs.
            completedCallbacks = _completedCallbacks.FindAll(static r => !r.IsDisposed);
            failedCallbacks = _failedCallbacks.FindAll(static r => !r.IsDisposed);
            _completedCallbacks = [];
            _failedCallbacks = [];
            // Most units never call GetOrAdd; skip the copy on the framework's most frequent path.
            scopeState = _scopeState.Count == 0 ? [] : [.. _scopeState.Values];
            _scopeState.Clear();
        }

        claim = new UnitOfWorkTerminalClaim(this, completedCallbacks, failedCallbacks, scopeState);
        Failure = failure;

        return true;
    }

    /// <summary>Throws unless the unit is still <see cref="UnitOfWorkState.Active" />.</summary>
    internal void ThrowIfNotActive()
    {
        var state = State;

        if (state != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException(
                $"The unit of work is {state}; registrations are accepted only while it is Active."
            );
        }
    }

    private static void _ThrowIfAny(List<Exception> exceptions)
    {
        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(exceptions);
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
    /// drain. Exactly one callback list is drained, per the claimed outcome.
    /// </summary>
    internal readonly struct UnitOfWorkTerminalClaim(
        UnitOfWork unit,
        List<CompletedRegistration> completedCallbacks,
        List<FailedRegistration> failedCallbacks,
        List<object> scopeState
    )
    {
        public UnitOfWork Unit { get; } = unit;

        public List<CompletedRegistration> CompletedCallbacks { get; } = completedCallbacks;

        public List<FailedRegistration> FailedCallbacks { get; } = failedCallbacks;

        public List<object> ScopeState { get; } = scopeState;
    }

    internal sealed class CompletedRegistration(Func<ValueTask> work) : IDisposable
    {
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public Func<ValueTask> Work { get; } = work;

        public void Dispose()
        {
            Volatile.Write(ref _disposed, 1);
        }
    }

    internal sealed class FailedRegistration(Func<UnitOfWorkFailure, ValueTask> work) : IDisposable
    {
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public Func<UnitOfWorkFailure, ValueTask> Work { get; } = work;

        public void Dispose()
        {
            Volatile.Write(ref _disposed, 1);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A unit-of-work failure callback faulted and was ignored: {FaultMessage}."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogFailureCallbackFaulted(ILogger logger, string faultMessage, Exception exception);
}
