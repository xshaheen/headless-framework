// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Tests.Internal;

/// <summary>
/// NSubstitute-based <see cref="IUnitOfWork"/> test doubles shared by the messaging delivery-decision,
/// publisher, and outbox tests. A resource-less substitute is enough for coordination-compatibility
/// scenarios that never drive a real transaction; the caller configures further behavior (callbacks,
/// <c>GetOrAdd</c>) per test.
/// </summary>
internal static class FakeUnitOfWorks
{
    /// <summary>
    /// An active, resource-less unit of work — compatible with in-memory-style storages. Backed by
    /// <see cref="FakeUnitOfWork"/> (not a bare NSubstitute fake) so <c>GetOrAdd</c>/<c>OnCompleted</c> drive
    /// real behavior for tests that exercise the coordinated write path end to end.
    /// </summary>
    public static IUnitOfWork CreateActive() => new FakeUnitOfWork();

    /// <summary>
    /// An active unit of work carrying a relational resource — incompatible with storages that require
    /// capturing rows on the unit itself (in-memory).
    /// </summary>
    public static IUnitOfWork CreateActiveWithRelationalResource() =>
        new FakeUnitOfWork { Resource = Substitute.For<IRelationalUnitOfWorkResource>() };
}

/// <summary>
/// A minimal, hand-rolled <see cref="IUnitOfWork"/> for tests that need real
/// <see cref="OnCompleted"/>/<see cref="OnFailed"/>/<see cref="GetOrAdd{TState}(Func{IUnitOfWork,TState})"/>
/// drive semantics (the outbox buffer's promote-on-complete / discard-on-fail behavior) without standing up
/// the full <c>Headless.UnitOfWork</c> engine or a database.
/// </summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly List<Func<ValueTask>> _onCompleted = [];
    private readonly List<Func<UnitOfWorkFailure, ValueTask>> _onFailed = [];
    private readonly Dictionary<Type, object> _state = [];

    public UnitOfWorkState State { get; private set; } = UnitOfWorkState.Active;

    public UnitOfWorkFailure? Failure { get; private set; }

    public IUnitOfWorkResource? Resource { get; init; }

    public bool IsRetryPrevented { get; private set; }

    public IDisposable OnCompleted(Func<ValueTask> work)
    {
        _onCompleted.Add(work);

        return new _Unregister(() => _onCompleted.Remove(work));
    }

    public IDisposable OnFailed(Func<UnitOfWorkFailure, ValueTask> work)
    {
        _onFailed.Add(work);

        return new _Unregister(() => _onFailed.Remove(work));
    }

    public TState GetOrAdd<TState>(Func<IUnitOfWork, TState> factory)
        where TState : class
    {
        if (_state.TryGetValue(typeof(TState), out var existing))
        {
            return (TState)existing;
        }

        var created = factory(this);
        _state[typeof(TState)] = created;

        return created;
    }

    public TState GetOrAdd<TState, TArg>(TArg arg, Func<IUnitOfWork, TArg, TState> factory)
        where TState : class
    {
        if (_state.TryGetValue(typeof(TState), out var existing))
        {
            return (TState)existing;
        }

        var created = factory(this, arg);
        _state[typeof(TState)] = created;

        return created;
    }

    /// <summary>No features here: the messaging tests drive the unit directly, never a feature.</summary>
    public TFeature? GetFeature<TFeature>()
        where TFeature : class, IUnitOfWorkFeature => null;

    public void PreventRetry() => IsRetryPrevented = true;

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        State = UnitOfWorkState.Completed;
        foreach (var work in _onCompleted)
        {
            await work().ConfigureAwait(false);
        }
    }

    public async ValueTask RollbackAsync()
    {
        if (State is not UnitOfWorkState.Active)
        {
            return;
        }

        State = UnitOfWorkState.Failed;
        Failure = new UnitOfWorkFailure(UnitOfWorkFailureReason.RolledBack);
        foreach (var work in _onFailed)
        {
            await work(Failure).ConfigureAwait(false);
        }
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class _Unregister(Action unregister) : IDisposable
    {
        public void Dispose() => unregister();
    }
}
