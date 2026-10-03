// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// <see cref="IDbContextFactory{TContext}"/> for a non-pooled Headless context (<see cref="HeadlessDbContext"/> or
/// the Identity context). Creates a dedicated service scope per call, resolves the context from it so a derived
/// constructor may take scoped services, and hands scope ownership to the returned context: disposing the context
/// disposes the scope.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>AddHeadlessDbContext&lt;TDbContext&gt;</c>. A pooled registration
/// (<c>AddHeadlessDbContextPool</c>) uses EF's <c>PooledDbContextFactory</c> instead: a pooled context's constructor
/// takes no scoped services, so its lease opens a private scope only when it first needs a scoped collaborator.
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
            return _CreateInScope(scope);
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
            return _CreateInScope(scope);
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

    private static TDbContext _CreateInScope(IServiceScope scope)
    {
        // The Headless registration binds the context to the scope that resolves it; ownership moves on top of that
        // binding, so it is released first and re-bound as owned.
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        if (context is not IHeadlessDbContextRuntimeOwner owner)
        {
            throw new InvalidOperationException(
                $"`{typeof(TDbContext).FullName}` must derive from HeadlessDbContext or HeadlessIdentityDbContext "
                    + "to be created through HeadlessDbContextFactory."
            );
        }

        owner.Runtime.Release();
        owner.Runtime.BindOwnedScope(scope);

        return context;
    }
}
