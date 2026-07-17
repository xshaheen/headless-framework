// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> for the Headless DbContext bases (both
/// <see cref="HeadlessDbContext"/> and the Identity context). Creates a dedicated service scope per call so
/// the context's scoped dependencies (<see cref="HeadlessDbContextServices"/>, save-changes pipeline, audit
/// persistence) resolve cleanly, then hands scope ownership to the returned context via
/// the internal <see cref="IHeadlessDbContextScopeOwner"/> seam — disposing the context disposes the scope.
/// </summary>
/// <remarks>
/// <para>
/// Why not <c>AddPooledDbContextFactory</c>: the Headless contexts are explicitly non-poolable (private
/// per-request runtime state, non-standard constructor shape). Why not the stock <c>AddDbContextFactory</c>:
/// it Activator-creates contexts from a singleton <see cref="DbContextOptions{TContext}"/>, which doesn't
/// compose with the constructor's required scoped <see cref="HeadlessDbContextServices"/> parameter.
/// </para>
/// <para>
/// Registered as singleton from <c>AddHeadlessDbContext&lt;TDbContext&gt;</c>. Consumers resolve
/// <see cref="IDbContextFactory{TContext}"/> and use the standard EF Core contract: dispose what you
/// create.
/// </para>
/// </remarks>
internal sealed class HeadlessDbContextFactory<TDbContext>(IServiceScopeFactory scopeFactory)
    : IDbContextFactory<TDbContext>
    where TDbContext : DbContext, IHeadlessDbContext
{
    public TDbContext CreateDbContext()
    {
        var scope = scopeFactory.CreateScope();

        try
        {
            return _AttachScope(scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Explicit override so callers awaiting <see cref="IDbContextFactory{TContext}.CreateDbContextAsync"/>
    /// observe the cancellation token and get async disposal on the failure path. The default interface
    /// implementation wraps <see cref="CreateDbContext"/> in <c>Task.FromResult</c> — dropping the token and
    /// disposing a failed scope synchronously, which throws for async-only-disposable scoped services.
    /// </summary>
    public async Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scope = scopeFactory.CreateAsyncScope();

        try
        {
            return _AttachScope(scope);
        }
        catch
        {
            // AsyncServiceScope.DisposeAsync async-disposes the scope, falling back to sync Dispose only for
            // services that are merely IDisposable — so a scoped service instantiated before the failure that
            // is async-only-disposable releases correctly instead of throwing (sync Dispose() would), which
            // would otherwise mask the original resolution exception.
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static TDbContext _AttachScope(IServiceScope scope)
    {
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Hand scope ownership through the internal seam — OwnedScope is not on the public IHeadlessDbContext.
        // Every Headless context base implements IHeadlessDbContextScopeOwner; a context that implements
        // IHeadlessDbContext by hand (without deriving from HeadlessDbContext/HeadlessIdentityDbContext) cannot
        // own a factory scope — fail with an actionable message rather than a bare InvalidCastException.
        if (context is not IHeadlessDbContextScopeOwner scopeOwner)
        {
            throw new InvalidOperationException(
                $"`{typeof(TDbContext).FullName}` must derive from HeadlessDbContext or HeadlessIdentityDbContext "
                    + "to be created through HeadlessDbContextFactory."
            );
        }

        scopeOwner.OwnedScope = scope;

        return context;
    }
}
