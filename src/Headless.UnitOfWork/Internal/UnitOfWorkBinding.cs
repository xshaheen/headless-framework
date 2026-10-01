// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// A weak <typeparamref name="TKey" /> → <see cref="IUnitOfWork" /> map: the explicit, non-ambient way a unit
/// reaches code that was handed only the object the unit was begun on (a <c>DbContext</c>, a
/// <c>DbConnection</c>). The key is the object both layers share, so a callee that runs a block on the same
/// key joins the caller's unit instead of guessing at one.
/// </summary>
/// <remarks>
/// A unit that can no longer host work is never handed back: the lookups evict a terminal unit, and they evict
/// and abandon an owned unit whose transaction ended outside the unit's own verbs — the shape a
/// begun-and-forgotten handle takes once a pooled context is reset or its transaction disposed by hand — so a
/// reused key falls back to a fresh begin instead of joining a transaction that no longer exists. Binding
/// replaces any previous entry; the providers refuse a second begin on a live key before they get here.
/// <para>
/// The abandon rolls the stale transaction back and, when the unit opened the connection, closes it. A caller
/// that is already asynchronous uses <see cref="TryGetAsync" /> so that close has finished before it begins on
/// the same connection; the synchronous <see cref="TryGet" /> backs the <c>UnitOfWork()</c> accessors and the
/// save pipeline's lookup, where the abandon runs in the background because nothing begins on the key next.
/// </para>
/// </remarks>
internal sealed class UnitOfWorkBinding<TKey>
    where TKey : class
{
    private readonly ConditionalWeakTable<TKey, IUnitOfWork> _bindings = [];

    /// <summary>Records (or replaces) the unit bound to <paramref name="key" />.</summary>
    public void Bind(TKey key, IUnitOfWork unit) => _bindings.AddOrUpdate(key, unit);

    /// <summary>
    /// Gets the unit bound to <paramref name="key" /> when it is still <see cref="UnitOfWorkState.Active" /> and,
    /// for an owned unit, its transaction is still open; anything else is evicted, and an owned unit that
    /// outlived its own transaction is abandoned in the background so a handle someone kept refuses further
    /// registrations.
    /// </summary>
    public bool TryGet(TKey key, out IUnitOfWork unit)
    {
        var stale = _Lookup(key, out unit);

        // Abandoning claims the terminal state synchronously (the rollback is a no-op on a finished transaction),
        // releases the unit-local state, and makes a retained handle refuse OnCompleted/GetOrAdd, which is the loud
        // failure the forgotten owner should see. An observed unit is left alone: its transaction normally ends
        // before the caller's CompleteAsync, so that shape is not a leak.
        stale?.Dispose();

        return unit is not null;
    }

    /// <summary>
    /// <see cref="TryGet" /> for an asynchronous caller about to begin on the same key: a stale owned unit is
    /// abandoned inline, so its rollback and connection close have finished before the caller opens the connection
    /// again and reads its state.
    /// </summary>
    public async ValueTask<IUnitOfWork?> TryGetAsync(TKey key)
    {
        var stale = _Lookup(key, out var unit);

        if (stale is not null)
        {
            await stale.DisposeAsync().ConfigureAwait(false);
        }

        return unit;
    }

    /// <summary>
    /// Resolves the live unit for <paramref name="key" /> into <paramref name="unit" /> (<see langword="null" />
    /// when none), evicting anything else, and returns the owned unit whose transaction ended behind its back
    /// so the caller can abandon it: nobody will complete it, and joining it would run the block outside any
    /// transaction and park enlisted rows on a unit that never drains.
    /// </summary>
    private IUnitOfWork? _Lookup(TKey key, out IUnitOfWork unit)
    {
        unit = null!;

        if (!_bindings.TryGetValue(key, out var bound))
        {
            return null;
        }

        if (bound.State == UnitOfWorkState.Active)
        {
            if (bound.Resource is not { IsOwned: true, IsTransactionCompleted: true })
            {
                unit = bound;

                return null;
            }

            _bindings.Remove(key);

            return bound;
        }

        _bindings.Remove(key);

        return null;
    }
}
