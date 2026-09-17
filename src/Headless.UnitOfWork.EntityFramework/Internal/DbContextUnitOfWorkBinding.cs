// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The <see cref="DbContext" /> → <see cref="IUnitOfWork" /> binding (KD13): <c>BeginAsync(db)</c> and
/// <c>Enlist(db, transaction)</c> record the unit so a context whose scope has no manager (a
/// <c>IDbContextFactory&lt;T&gt;</c>-created context owning its own scope) is still resolvable by the save
/// pipeline. Internal plumbing; exposed to consumers through the hidden <see cref="DbContextUnitOfWork" />
/// surface.
/// </summary>
internal static class DbContextUnitOfWorkBinding
{
    private static readonly ConditionalWeakTable<DbContext, IUnitOfWork> _Bindings = [];

    /// <summary>Records (or replaces) the unit bound to <paramref name="db" />.</summary>
    public static void Bind(DbContext db, IUnitOfWork unit) => _Bindings.AddOrUpdate(db, unit);

    /// <summary>
    /// Gets the unit bound to <paramref name="db" /> when it is still <see cref="UnitOfWorkState.Active" />;
    /// a terminal unit is ignored and evicted (the binding must never resurrect a finished unit).
    /// </summary>
    public static bool TryGet(DbContext db, out IUnitOfWork unit)
    {
        if (_Bindings.TryGetValue(db, out var bound) && bound.State == UnitOfWorkState.Active)
        {
            unit = bound;

            return true;
        }

        if (bound is not null)
        {
            _Bindings.Remove(db);
        }

        unit = null!;

        return false;
    }

    /// <summary>Removes the binding for <paramref name="db" /> (the unit reached a terminal state).</summary>
    public static void Unbind(DbContext db) => _Bindings.Remove(db);
}
