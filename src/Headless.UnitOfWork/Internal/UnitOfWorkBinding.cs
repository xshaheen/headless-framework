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
/// A unit that can no longer host work is never handed back: <see cref="TryGet" /> evicts a terminal unit, and
/// it evicts and abandons an owned unit whose transaction ended outside the unit's own verbs — the shape a
/// begun-and-forgotten handle takes once a pooled context is reset or its transaction disposed by hand — so a
/// reused key falls back to a fresh begin instead of joining a transaction that no longer exists. Binding
/// replaces any previous entry; the providers refuse a second begin on a live key before they get here.
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
    /// outlived its own transaction is abandoned so a handle someone kept refuses further registrations.
    /// </summary>
    public bool TryGet(TKey key, out IUnitOfWork unit)
    {
        if (!_bindings.TryGetValue(key, out var bound))
        {
            unit = null!;

            return false;
        }

        if (bound.State == UnitOfWorkState.Active)
        {
            if (bound.Resource is not { IsOwned: true, IsTransactionCompleted: true })
            {
                unit = bound;

                return true;
            }

            // Active in memory, but the transaction the unit owns has already ended without going through the
            // unit's verbs (a pooled context reset it, or the caller disposed it by hand): nobody will complete
            // it, so joining it would run the block outside any transaction and park enlisted rows on a unit that
            // never drains. Abandoning it claims the terminal state (the rollback is a no-op on a finished
            // transaction), releases its unit-local state, and makes a retained handle refuse OnCompleted/GetOrAdd,
            // which is the loud failure the forgotten owner should see. An observed unit is left alone: its
            // transaction normally ends before the caller's CompleteAsync, so that shape is not a leak.
            bound.Dispose();
        }

        _bindings.Remove(key);
        unit = null!;

        return false;
    }
}
