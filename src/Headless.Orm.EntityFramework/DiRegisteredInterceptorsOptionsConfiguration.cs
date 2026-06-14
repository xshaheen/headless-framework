// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.EntityFramework;

/// <summary>
/// A DI-registered <see cref="IDbContextOptionsConfiguration{TContext}"/> that attaches every application-registered
/// <see cref="IInterceptor"/> to <typeparamref name="TContext"/>'s options while EF Core builds them. EF Core does
/// not auto-discover interceptors from the application container, so this is the seam that makes package-registered
/// interceptors (e.g. the commit-coordination transaction interceptor) fire — and, unlike a one-off
/// <c>AddInterceptors</c> call inside a single <c>AddDbContext</c> lambda, it applies whenever EF Core builds
/// options for <typeparamref name="TContext"/>, including a consumer's own plain <c>AddDbContext&lt;TContext&gt;</c>.
/// </summary>
/// <remarks>
/// Instances already present on the builder's <see cref="CoreOptionsExtension.Interceptors"/> (e.g. added by the
/// consumer's own options action, or by another registered configuration) are skipped by reference equality, so an
/// interceptor never runs twice per edge. Interceptors are expected to be singletons (the framework's own are).
/// </remarks>
internal sealed class DiRegisteredInterceptorsOptionsConfiguration<TContext>(IEnumerable<IInterceptor> interceptors)
    : IDbContextOptionsConfiguration<TContext>
    where TContext : DbContext
{
    public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder)
    {
        var existing = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.Interceptors;

        var missing = interceptors
            .Where(interceptor => existing?.Any(e => ReferenceEquals(e, interceptor)) != true)
            .ToArray();

        if (missing.Length > 0)
        {
            optionsBuilder.AddInterceptors(missing);
        }
    }
}
