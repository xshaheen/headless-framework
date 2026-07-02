// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>
/// Abstract base for an ASP.NET Core Identity <see cref="DbContext"/> that integrates with the Headless
/// framework runtime, using <see cref="IdentityUserPasskey{TKey}"/> as the default passkey entity.
/// </summary>
/// <typeparam name="TUser">The type used to represent a user.</typeparam>
/// <typeparam name="TRole">The type used to represent a role.</typeparam>
/// <typeparam name="TKey">The type used for the primary key of users and roles.</typeparam>
/// <typeparam name="TUserClaim">The type used to represent a claim that is possessed by a user.</typeparam>
/// <typeparam name="TUserRole">The type used to represent the link between a user and a role.</typeparam>
/// <typeparam name="TUserLogin">The type used to represent an external login from a third-party provider.</typeparam>
/// <typeparam name="TRoleClaim">The type used to represent a claim that is assigned to a role.</typeparam>
/// <typeparam name="TUserToken">The type used to represent an authentication token for a user.</typeparam>
/// <remarks>
/// Delegates to <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken,TUserPasskey}"/>
/// with <c>TUserPasskey</c> fixed to <see cref="IdentityUserPasskey{TKey}"/>. Use the nine-type-parameter
/// variant when you need a custom passkey entity.
/// </remarks>
/// <remarks>
/// Initializes a new instance of <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken}"/>.
/// </remarks>
/// <param name="services">
/// The Headless services bundle injected by the DI container; provides the runtime, current user,
/// tenant context, and save-pipeline dependencies.
/// </param>
/// <param name="options">The EF Core options for this context.</param>
public abstract class HeadlessIdentityDbContext<
    TUser,
    TRole,
    TKey,
    TUserClaim,
    TUserRole,
    TUserLogin,
    TRoleClaim,
    TUserToken
>(HeadlessDbContextServices services, DbContextOptions options)
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
    >(services, options)
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserClaim : IdentityUserClaim<TKey>
    where TUserRole : IdentityUserRole<TKey>
    where TUserLogin : IdentityUserLogin<TKey>
    where TRoleClaim : IdentityRoleClaim<TKey>
    where TUserToken : IdentityUserToken<TKey>;

/// <summary>
/// Abstract base for an ASP.NET Core Identity <see cref="DbContext"/> that integrates with the full
/// Headless framework runtime: multi-tenancy, audit trail, ambient transactions, domain-event dispatch,
/// and the coordinated save-changes pipeline.
/// </summary>
/// <typeparam name="TUser">The type used to represent a user.</typeparam>
/// <typeparam name="TRole">The type used to represent a role.</typeparam>
/// <typeparam name="TKey">The type used for the primary key of users and roles.</typeparam>
/// <typeparam name="TUserClaim">The type used to represent a claim that is possessed by a user.</typeparam>
/// <typeparam name="TUserRole">The type used to represent the link between a user and a role.</typeparam>
/// <typeparam name="TUserLogin">The type used to represent an external login from a third-party provider.</typeparam>
/// <typeparam name="TRoleClaim">The type used to represent a claim that is assigned to a role.</typeparam>
/// <typeparam name="TUserToken">The type used to represent an authentication token for a user.</typeparam>
/// <typeparam name="TUserPasskey">The type used to represent a passkey associated with a user.</typeparam>
/// <remarks>
/// <para>
/// Extends the standard ASP.NET Core Identity <c>IdentityDbContext</c> with Headless-specific behaviour:
/// <list type="bullet">
///   <item>Applies <see cref="DefaultSchema"/> to all Identity tables so callers can isolate their
///   Identity schema without a custom <c>OnModelCreating</c> override.</item>
///   <item>Routes all <c>SaveChanges</c> / <c>SaveChangesAsync</c> calls through the Headless
///   save-pipeline (audit stamping, domain-event dispatch, coordinated outbox writes).</item>
///   <item>Exposes the resolved <see cref="TenantId"/> from the ambient tenant context so
///   multi-tenant filters can reference it without a separate service resolution.</item>
///   <item>Manages the optional <c>IServiceScope</c> created by
///   <c>HeadlessDbContextFactory</c> when the context is resolved via
///   <c>IDbContextFactory</c>, preventing scope leaks on factory-created instances.</item>
/// </list>
/// </para>
/// <para>
/// Register via <c>IServiceCollection.AddHeadlessDbContext&lt;TDbContext,...&gt;</c> from
/// <c>SetupIdentityEntityFramework</c> — do not call <c>AddDbContext</c> directly, as the
/// Headless wiring (interceptors, factory, service bundle) will be missing.
/// </para>
/// </remarks>
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
        IHeadlessDbContext,
        IHeadlessDbContextScopeOwner
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

    /// <summary>
    /// Gets the default database schema applied to all Identity tables created by this context,
    /// or <see langword="null"/> to use the database provider's default schema.
    /// </summary>
    public abstract string? DefaultSchema { get; }

    /// <summary>
    /// Gets the tenant identifier resolved from the ambient tenant context at the time this
    /// context was created, or <see langword="null"/> when running outside a tenant scope.
    /// </summary>
    public string? TenantId => _runtime.TenantId;

    // Optional service scope owned by this context — set by HeadlessDbContextFactory (via the
    // IHeadlessDbContext seam) when the context is created through IDbContextFactory<TDbContext>.
    // Disposed alongside the context so factory-created contexts don't leak per-call scopes.
    private IServiceScope? _ownedScope;

    // The IHeadlessDbContext seam is implemented explicitly (non-overridable) so it stays off this context's
    // public surface and avoids an externally-overridable member bound to the seam (CA2119). CA1033 (explicit
    // member not visible to derived types) is intentional: derived contexts never call these — the framework
    // runtime/save pipeline and coordinated-transaction helpers reach them through the interface.
#pragma warning disable CA1033
    string? IHeadlessDbContext.DefaultSchema => DefaultSchema;

    string? IHeadlessDbContext.TenantId => TenantId;

    IServiceProvider IHeadlessDbContext.ServiceProvider => _runtime.ServiceProvider;

    IServiceScope? IHeadlessDbContextScopeOwner.OwnedScope
    {
        get => _ownedScope;
        set => _ownedScope = value;
    }
#pragma warning restore CA1033

    /// <summary>
    /// Initializes a new instance of
    /// <see cref="HeadlessIdentityDbContext{TUser,TRole,TKey,TUserClaim,TUserRole,TUserLogin,TRoleClaim,TUserToken,TUserPasskey}"/>.
    /// </summary>
    /// <param name="services">
    /// The Headless services bundle injected by the DI container; provides the runtime, current user,
    /// tenant context, and save-pipeline dependencies.
    /// </param>
    /// <param name="options">The EF Core options for this context.</param>
    protected HeadlessIdentityDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : base(options)
    {
        _runtime = new(this, services);
        _runtime.Initialize();
    }

    /// <summary>
    /// Saves all pending changes to the database, running the Headless save-pipeline
    /// (audit stamping, domain-event dispatch, outbox writes) before the underlying EF Core call.
    /// </summary>
    /// <returns>The number of state entries written to the database.</returns>
    public override int SaveChanges()
    {
        return _runtime.SaveChanges(base.SaveChanges, acceptAllChangesOnSuccess: true);
    }

    /// <summary>
    /// Saves all pending changes to the database, running the Headless save-pipeline
    /// (audit stamping, domain-event dispatch, outbox writes) before the underlying EF Core call.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">
    /// Indicates whether <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker.AcceptAllChanges"/>
    /// is called after the changes have been sent to the database.
    /// </param>
    /// <returns>The number of state entries written to the database.</returns>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return _runtime.SaveChanges(base.SaveChanges, acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Asynchronously saves all pending changes to the database, running the Headless save-pipeline
    /// (audit stamping, domain-event dispatch, outbox writes) before the underlying EF Core call.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the save operation.</param>
    /// <returns>A task that resolves to the number of state entries written to the database.</returns>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _runtime.SaveChangesAsync(base.SaveChangesAsync, acceptAllChangesOnSuccess: true, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves all pending changes to the database, running the Headless save-pipeline
    /// (audit stamping, domain-event dispatch, outbox writes) before the underlying EF Core call.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">
    /// Indicates whether <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker.AcceptAllChanges"/>
    /// is called after the changes have been sent to the database.
    /// </param>
    /// <param name="cancellationToken">A token that can be used to cancel the save operation.</param>
    /// <returns>A task that resolves to the number of state entries written to the database.</returns>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        return _runtime.SaveChangesAsync(base.SaveChangesAsync, acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Releases resources held by the context, including the Headless runtime and any service scope
    /// created by <c>HeadlessDbContextFactory</c>.
    /// </summary>
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

    /// <summary>
    /// Asynchronously releases resources held by the context, including the Headless runtime and any
    /// service scope created by <c>HeadlessDbContextFactory</c>.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that completes when all resources have been released.</returns>
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

    /// <summary>
    /// Applies Headless-specific model conventions (for example, value-object conversions) in
    /// addition to the Identity schema conventions supplied by the base class.
    /// </summary>
    /// <param name="configurationBuilder">The builder used to configure model conventions.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        HeadlessDbContextRuntime.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Configures the EF Core model for this context, applying <see cref="DefaultSchema"/> when set,
    /// then delegating to the base Identity model builder and the Headless runtime for additional
    /// configurations such as query filters and audit-entity mappings.
    /// </summary>
    /// <param name="builder">The builder used to construct the model for this context.</param>
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
