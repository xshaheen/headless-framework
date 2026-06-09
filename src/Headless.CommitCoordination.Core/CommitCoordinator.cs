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

    internal bool IsRoot => ReferenceEquals(this, Root);

    internal CommitCoordinator CreateChild()
    {
        _ThrowIfNotActive();

        return new CommitCoordinator(this);
    }

    /// <inheritdoc />
    public IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work)
    {
        Argument.IsNotNull(work);

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

        if (_parent is not null)
        {
            return Root.GetOrAdd(factory);
        }

        lock (_gate)
        {
            _ThrowIfNotActive();

            var type = typeof(TBuffer);

            if (_buffers.TryGetValue(type, out var existing))
            {
                return (TBuffer)existing;
            }

            var buffer = factory(this);
            _buffers.Add(type, buffer);

            return buffer;
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

    internal async ValueTask SignalAsync(
        CommitOutcome outcome,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        if (_parent is not null)
        {
            if (outcome == CommitOutcome.RolledBack)
            {
                await Root.SignalAsync(outcome, services, cancellationToken).ConfigureAwait(false);
                _SetTerminal(CommitCoordinatorState.RolledBack);
                return;
            }

            _SetTerminal(CommitCoordinatorState.Committed);
            return;
        }

        var terminalState = outcome == CommitOutcome.Committed
            ? CommitCoordinatorState.Committed
            : CommitCoordinatorState.RolledBack;

        if (Interlocked.CompareExchange(ref _state, (int)CommitCoordinatorState.Draining, (int)CommitCoordinatorState.Active) != (int)CommitCoordinatorState.Active)
        {
            // First terminal signal already won (D10): a second signal — e.g. EF TransactionCommitted racing the
            // SqlClient diagnostic, or a Dispose racing a Rollback — is an ignored no-op, surfaced so provider
            // double-signal bugs stay diagnosable instead of silent.
            LogIgnoredRacingSignal(_logger, State, outcome);

            return;
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

        var context = new CommitContext(_capabilities)
        {
            Services = services,
            Outcome = outcome,
        };

        var exceptions = new List<Exception>();

        foreach (var registration in callbacks)
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

        try
        {
            await _DisposeBuffersAsync(buffers, exceptions).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _state, (int)terminalState);
        }

        cancellationToken.ThrowIfCancellationRequested();

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
        if (_parent is not null && State == CommitCoordinatorState.Committed)
        {
            return;
        }

        foreach (var registration in _promotedRegistrations)
        {
            registration.Dispose();
        }
    }

    internal void MarkRolledBack()
    {
        _SetTerminal(CommitCoordinatorState.RolledBack);
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
                if (typeof(ICommitCapability).IsAssignableFrom(contract))
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

    private sealed class CommitCallbackRegistration(Func<CommitContext, CancellationToken, ValueTask> work)
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
}
