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

    /// <summary>
    /// Validates, when <c>UseApplicationDbContext</c> is called, that Jobs can construct <typeparamref name="TContext" />
    /// itself. Jobs creates its contexts outside any request scope: from a pooled factory, and by cloning the options
    /// onto a unit of work's connection. Both need the single-argument <c>DbContextOptions&lt;TContext&gt;</c>
    /// constructor. A context deriving from <c>HeadlessDbContext</c> cannot declare one, because it takes
    /// request-scoped services, so the message names the supported alternative instead of only the constructor.
    /// </summary>
    public static void RequireApplicationContextConstructor<TContext>()
        where TContext : DbContext
    {
        if (typeof(TContext).GetConstructor([typeof(DbContextOptions<TContext>)]) is not null)
        {
            return;
        }

        var name = typeof(TContext).Name;

        throw new InvalidOperationException(
            $"Jobs cannot share {name} through UseApplicationDbContext. Jobs creates its contexts outside any request "
                + $"scope (from a pooled factory, and cloned onto a unit of work's connection), so {name} must declare a "
                + $"public constructor accepting a single DbContextOptions<{name}> argument. A context deriving from "
                + "HeadlessDbContext cannot, because it takes request-scoped services (the current tenant and the save "
                + "pipeline). Register a dedicated Jobs context with UseJobsDbContext<TJobsContext>() instead, where "
                + "TJobsContext derives from JobsDbContext<TTimeJob, TCronJob>; it can use the same database."
        );
    }
}
