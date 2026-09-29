// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
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
            await _PinTenantPlacementAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// For a tenant-routed context type under an ambient tenant, resolves the tenant's placement and pins it in the
    /// new scope before the context is built, because the context builds its model (and so needs the schema) inside
    /// its constructor, where nothing can be awaited. A tenant with no placement is refused: a routed context never
    /// falls back to the shared database. Resolver faults and cancellation propagate unchanged.
    /// </summary>
    private static async Task _PinTenantPlacementAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetService<HeadlessTenantDataRouting>()?.IsRouted(typeof(TDbContext)) != true)
        {
            return;
        }

        var tenantId = services.GetRequiredService<ICurrentTenant>().Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        // Reuse what the tenancy entry point already resolved for this tenant, fault included.
        var placement =
            (
                TenantDataPlacementPreloader.TryGetPreloaded(tenantId, out var preloaded)
                    ? preloaded
                    : await services
                        .GetRequiredService<ITenantDataPlacementResolver>()
                        .ResolveAsync(tenantId, cancellationToken)
                        .ConfigureAwait(false)
            ) ?? throw HeadlessRoutedPlacement.NoPlacement(tenantId, typeof(TDbContext));

        services.GetRequiredService<HeadlessTenantPlacementPin>().Set(tenantId, placement);
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
