// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

public abstract class HeadlessIdentityDbContext<
    TUser,
    TRole,
    TKey,
    TUserClaim,
    TUserRole,
    TUserLogin,
    TRoleClaim,
    TUserToken
>
    : HeadlessIdentityDbContext<
        TUser,
        TRole,
        TKey,
        TUserClaim,
        TUserRole,
        TUserLogin,
        TRoleClaim,
        TUserToken,
        IdentityUserPasskey<TKey>
    >
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserClaim : IdentityUserClaim<TKey>
    where TUserRole : IdentityUserRole<TKey>
    where TUserLogin : IdentityUserLogin<TKey>
    where TRoleClaim : IdentityRoleClaim<TKey>
    where TUserToken : IdentityUserToken<TKey>
{
    protected HeadlessIdentityDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : base(services, options) { }
}

public abstract class HeadlessIdentityDbContext<
    TUser,
    TRole,
    TKey,
    TUserClaim,
    TUserRole,
    TUserLogin,
    TRoleClaim,
    TUserToken,
    TUserPasskey
>
    : IdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>,
        IHeadlessDbContext
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserClaim : IdentityUserClaim<TKey>
    where TUserRole : IdentityUserRole<TKey>
    where TUserLogin : IdentityUserLogin<TKey>
    where TRoleClaim : IdentityRoleClaim<TKey>
    where TUserToken : IdentityUserToken<TKey>
    where TUserPasskey : IdentityUserPasskey<TKey>
{
    private readonly HeadlessDbContextRuntime _runtime;

    public abstract string? DefaultSchema { get; }

    public string? TenantId => _runtime.TenantId;

    // Optional service scope owned by this context — set by HeadlessDbContextFactory (via the
    // IHeadlessDbContext seam) when the context is created through IDbContextFactory<TDbContext>.
    // Disposed alongside the context so factory-created contexts don't leak per-call scopes.
    private IServiceScope? _ownedScope;

    // The IHeadlessDbContext seam is internal, so satisfy it through explicit (non-overridable)
    // implementations that delegate to the public members — keeps the public surface intact while avoiding
    // an externally-overridable member bound to an internal interface (CA2119).
    string? IHeadlessDbContext.DefaultSchema => DefaultSchema;

    string? IHeadlessDbContext.TenantId => TenantId;

    IServiceProvider IHeadlessDbContext.ServiceProvider => _runtime.ServiceProvider;

    IServiceScope? IHeadlessDbContext.OwnedScope
    {
        get => _ownedScope;
        set => _ownedScope = value;
    }

    protected HeadlessIdentityDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : base(options)
    {
        _runtime = new(this, services);
        _runtime.Initialize();
    }

    public override int SaveChanges()
    {
        return _runtime.SaveChanges(base.SaveChanges, acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return _runtime.SaveChanges(base.SaveChanges, acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _runtime.SaveChangesAsync(base.SaveChangesAsync, acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        return _runtime.SaveChangesAsync(base.SaveChangesAsync, acceptAllChangesOnSuccess, cancellationToken);
    }

    public override void Dispose()
    {
        // Drain the runtime then the base context; try/finally guarantees the owned scope still disposes if
        // either throws. Owned-scope disposal is centralized in HeadlessDbContextDisposal so it stays
        // identical with the plain HeadlessDbContext base (previously this context disposed no owned scope).
        try
        {
            var disposeTask = _runtime.DisposeAsync();
            if (!disposeTask.IsCompletedSuccessfully)
            {
                disposeTask.AsTask().GetAwaiter().GetResult();
            }

            base.Dispose();
        }
        finally
        {
            this.DisposeOwnedScope();
            GC.SuppressFinalize(this);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await this.DisposeOwnedScopeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        HeadlessDbContextRuntime.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(DefaultSchema))
        {
            builder.HasDefaultSchema(DefaultSchema);
        }

        base.OnModelCreating(builder);
        _runtime.ProcessModelCreating(builder);
    }
}
