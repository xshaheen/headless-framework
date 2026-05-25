// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> for <see cref="HeadlessDbContext"/> subclasses. Creates a
/// dedicated service scope per call so the context's scoped dependencies
/// (<see cref="HeadlessDbContextServices"/>, save-changes pipeline, audit persistence) resolve cleanly,
/// then hands scope ownership to the returned context — disposing the context disposes the scope.
/// </summary>
/// <remarks>
/// <para>
/// Why not <c>AddPooledDbContextFactory</c>: <see cref="HeadlessDbContext"/> is explicitly non-poolable
/// (private per-request runtime state, non-standard constructor shape). Why not the stock
/// <c>AddDbContextFactory</c>: it Activator-creates contexts from a singleton
/// <see cref="DbContextOptions{TContext}"/>, which doesn't compose with the constructor's required
/// scoped <see cref="HeadlessDbContextServices"/> parameter.
/// </para>
/// <para>
/// Registered as singleton from <c>AddHeadlessDbContext&lt;TDbContext&gt;</c>. Consumers resolve
/// <see cref="IDbContextFactory{TContext}"/> and use the standard EF Core contract: dispose what you
/// create.
/// </para>
/// </remarks>
internal sealed class HeadlessDbContextFactory<TDbContext>(IServiceScopeFactory scopeFactory)
    : IDbContextFactory<TDbContext>
    where TDbContext : HeadlessDbContext
{
    public TDbContext CreateDbContext()
    {
        var scope = scopeFactory.CreateScope();

        try
        {
            var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
            context.OwnedScope = scope;
            return context;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
