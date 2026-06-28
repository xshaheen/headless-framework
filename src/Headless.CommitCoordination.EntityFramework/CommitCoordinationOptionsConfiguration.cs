// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// A DI-registered <see cref="IDbContextOptionsConfiguration{TContext}"/> that auto-attaches the
/// commit-coordination transaction interceptor to <typeparamref name="TContext"/>'s options while EF Core
/// builds them — including a plain <c>AddDbContext&lt;TContext&gt;</c> with no consumer
/// <c>AddInterceptors</c> call. EF Core does <b>not</b> auto-discover <see cref="IInterceptor"/>
/// registrations from the application service provider, so without this seam the commit/rollback edges are
/// never observed and coordinated work silently drains as rollback.
/// </summary>
/// <remarks>
/// Scoped deliberately to the <see cref="CommitCoordinationTransactionInterceptor"/> (resolved from the
/// injected <see cref="IInterceptor"/> registrations) rather than all DI interceptors, to avoid surprising
/// attachment of unrelated interceptors registered for other reasons. The commit interceptor is a singleton,
/// so the EF scoped-interceptor limitation does not apply. Instances already present on the builder's
/// <see cref="CoreOptionsExtension.Interceptors"/> are skipped by reference equality so the interceptor never
/// runs twice per edge.
/// </remarks>
internal sealed class CommitCoordinationOptionsConfiguration<TContext>(IEnumerable<IInterceptor> interceptors)
    : IDbContextOptionsConfiguration<TContext>
    where TContext : DbContext
{
    private readonly IEnumerable<IInterceptor> _interceptors = Argument.IsNotNull(interceptors);

    public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
    {
        Argument.IsNotNull(optionsBuilder);

        var existing = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;

        var missing = _interceptors
            .OfType<CommitCoordinationTransactionInterceptor>()
            .Where(interceptor => existing?.Any(e => ReferenceEquals(e, interceptor)) != true)
            .Cast<IInterceptor>()
            .ToArray();

        if (missing.Length > 0)
        {
            optionsBuilder.AddInterceptors(missing);
        }
    }
}
