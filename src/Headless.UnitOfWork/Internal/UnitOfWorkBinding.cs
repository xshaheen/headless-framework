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
/// A terminal unit is never handed back: <see cref="TryGet" /> evicts it, so a pooled key can never resurrect a
/// finished unit. Binding replaces any previous entry; the providers refuse a second begin on a live key before
/// they get here.
/// </remarks>
internal sealed class UnitOfWorkBinding<TKey>
    where TKey : class
{
    private readonly ConditionalWeakTable<TKey, IUnitOfWork> _bindings = [];

    /// <summary>Records (or replaces) the unit bound to <paramref name="key" />.</summary>
    public void Bind(TKey key, IUnitOfWork unit) => _bindings.AddOrUpdate(key, unit);

    /// <summary>
    /// Gets the unit bound to <paramref name="key" /> when it is still <see cref="UnitOfWorkState.Active" />;
    /// a terminal unit is ignored and evicted.
    /// </summary>
    public bool TryGet(TKey key, out IUnitOfWork unit)
    {
        if (_bindings.TryGetValue(key, out var bound) && bound.State == UnitOfWorkState.Active)
        {
            unit = bound;

            return true;
        }

        if (bound is not null)
        {
            _bindings.Remove(key);
        }

        unit = null!;

        return false;
    }
}
