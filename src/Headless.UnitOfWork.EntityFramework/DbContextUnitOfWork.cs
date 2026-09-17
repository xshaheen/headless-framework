// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace

namespace Headless.UnitOfWork;

/// <summary>
/// The public-but-hidden accessor over the <see cref="DbContext" /> → <see cref="IUnitOfWork" /> binding
/// (KD13). The save pipeline (in <c>Headless.EntityFramework</c>, a different package) resolves the unit
/// bound to a context through <see cref="Find" /> before consulting the scope manager.
/// </summary>
/// <remarks>
/// Hidden from IntelliSense: application code opens units of work through
/// <see cref="IUnitOfWorkManager" /> members, never through this accessor.
/// </remarks>
[PublicAPI]
public static class DbContextUnitOfWork
{
    /// <summary>
    /// Gets the unit of work bound to <paramref name="db" /> while it is still
    /// <see cref="UnitOfWorkState.Active" />, or <see langword="null" /> when none is (no unit was begun on
    /// the context, or the bound unit reached a terminal state and was evicted).
    /// </summary>
    /// <param name="db">The context whose bound unit is sought.</param>
    /// <returns>The active bound unit, or <see langword="null" />.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IUnitOfWork? Find(DbContext db)
    {
        Headless.Checks.Argument.IsNotNull(db);

        return DbContextUnitOfWorkBinding.TryGet(db, out var unit) ? unit : null;
    }
}
