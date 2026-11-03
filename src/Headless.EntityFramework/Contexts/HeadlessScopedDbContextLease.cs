// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.EntityFramework;

/// <summary>
/// The scope's hold on one pooled Headless context: leases it from the pool, binds it to the scope, and returns it
/// once when the scope ends. The public counterpart of EF Core's internal scoped pool lease, which a consumer's early
/// <c>Dispose</c> cannot release either.
/// </summary>
internal sealed class HeadlessScopedDbContextLease<TDbContext> : IDisposable, IAsyncDisposable
    where TDbContext : DbContext, IHeadlessDbContext
{
    // True when the scope is the private scope a context opened for itself: the context is that context, which owns
    // the scope rather than being held by it, so the lease neither binds nor returns it.
    private readonly bool _borrowed;

    public HeadlessScopedDbContextLease(
        IDbContextFactory<TDbContext> factory,
        IServiceProvider services,
        HeadlessPrivateScopeOwner privateScopeOwner
    )
    {
        if (privateScopeOwner.Context is TDbContext owningContext)
        {
            Context = owningContext;
            _borrowed = true;

            return;
        }

        Context = factory.CreateDbContext();

        if (Context is not IHeadlessDbContextRuntimeOwner owner)
        {
            Context.Dispose();

            throw new InvalidOperationException(
                $"`{typeof(TDbContext).FullName}` must derive from HeadlessDbContext or HeadlessIdentityDbContext."
            );
        }

        // A scoped service receives the scope's own provider.
        owner.Runtime.BindScopeLease(services);
    }

    public TDbContext Context { get; }

    public void Dispose()
    {
        if (_borrowed)
        {
            return;
        }

        ((IHeadlessDbContextRuntimeOwner)Context).Runtime.EndScopeLease();
        Context.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_borrowed)
        {
            return ValueTask.CompletedTask;
        }

        ((IHeadlessDbContextRuntimeOwner)Context).Runtime.EndScopeLease();

        return Context.DisposeAsync();
    }
}
