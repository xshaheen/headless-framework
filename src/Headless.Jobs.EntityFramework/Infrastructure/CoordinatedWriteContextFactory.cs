// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Shared validation for the coordinated-write DbContext constructor contract. Lives outside
/// <see cref="JobsEfCorePersistenceProvider{TDbContext,TTimeJob,TCronJob}" /> on purpose: the persistence provider
/// caches the compiled constructor delegate in a static field initializer, so touching that type to validate the
/// constructor would surface a missing ctor as a <see cref="TypeInitializationException" /> wrapping the authored
/// message. Registration calls <see cref="RequireOptionsConstructor{TContext}" /> here first, so a misconfigured
/// context fails loud at DI-build time with the direct message instead.
/// </summary>
internal static class CoordinatedWriteContextFactory
{
    /// <summary>
    /// Returns the single-argument <c>DbContextOptions&lt;TContext&gt;</c> constructor coordinated writes require —
    /// the same constructor EF Core's DbContext pooling needs — or throws a direct
    /// <see cref="InvalidOperationException" /> when the context does not declare it.
    /// </summary>
    public static ConstructorInfo RequireOptionsConstructor<TContext>()
        where TContext : DbContext
    {
        return typeof(TContext).GetConstructor([typeof(DbContextOptions<TContext>)])
            ?? throw new InvalidOperationException(
                $"Coordinated job writes require {typeof(TContext).Name} to declare a public constructor accepting a "
                    + $"single DbContextOptions<{typeof(TContext).Name}> argument — the same constructor EF Core's "
                    + "DbContext pooling requires."
            );
    }
}
