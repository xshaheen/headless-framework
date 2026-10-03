// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.EntityFramework;

/// <summary>
/// Base <see cref="DbContext"/> with the framework's save pipeline, multi-tenancy filter,
/// auditing, soft-delete, and domain-event dispatch wired in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Poolable.</b> Register a subclass with <c>AddHeadlessDbContextPool</c> to pool it, or with
/// <c>AddHeadlessDbContext</c> to create one per scope. The constructor takes only <see cref="DbContextOptions"/>:
/// the model, the EF configuration, and the change-tracker handlers belong to the instance, while the scoped
/// collaborators (save pipeline, audit persistence, entry processors) are resolved lazily from the scope bound to
/// the current lease and released before a pooled instance returns to the pool.
/// </para>
/// <para>
/// A pooled subclass must declare exactly one public constructor, taking its <c>DbContextOptions</c> and optionally
/// singleton services, and must not keep per-request state in its own fields: EF resets only the state it knows
/// about. The ambient tenant is read from <c>ICurrentTenant</c> on every access, so it is never captured.
/// </para>
/// </remarks>
[PublicAPI]
public abstract class HeadlessDbContext : DbContext, IHeadlessDbContext, IHeadlessDbContextRuntimeOwner
{
    private readonly HeadlessDbContextRuntime _runtime;

    /// <summary>
    /// Initializes the context with the EF Core options. The Headless collaborators are resolved from the scope bound
    /// to the context, not injected here, which is what lets the context be pooled.
    /// </summary>
    /// <param name="options">The EF Core options for this context type.</param>
    protected HeadlessDbContext(DbContextOptions options)
        : base(options)
    {
        _runtime = new(this);
        _runtime.AttachChangeTrackerHandlers();
    }

    /// <summary>
    /// Returns the optional default database schema applied to entities that do not declare their own.
    /// Override in subclasses to set a schema (for example return <c>"myapp"</c>); return
    /// <see langword="null"/> to use the provider default.
    /// </summary>
    public abstract string? DefaultSchema { get; }

    /// <summary>
    /// Returns the active ambient <c>ICurrentTenant</c> identifier on each read.
    /// The multi-tenancy global query filter and the write guard both read this value.
    /// </summary>
    public string? TenantId => _runtime.TenantId;

    // The IHeadlessDbContext seam is implemented explicitly (non-overridable) so it stays off this context's
    // public surface and avoids an externally-overridable member bound to the seam (CA2119).
#pragma warning disable CA1033 // Derived contexts never call these; the framework reaches them through the interface.
    string? IHeadlessDbContext.DefaultSchema => DefaultSchema;

    string? IHeadlessDbContext.TenantId => TenantId;

    IServiceProvider IHeadlessDbContext.ServiceProvider => _runtime.ServiceProvider;

    HeadlessDbContextRuntime IHeadlessDbContextRuntimeOwner.Runtime => _runtime;
#pragma warning restore CA1033

    /// <summary>
    /// Runs the Headless save pipeline (processor chain, audit capture, domain-event dispatch,
    /// integration-event outbox enqueue) and then persists all changes to the database in a single
    /// transaction.
    /// </summary>
    /// <returns>The number of state entries written to the database.</returns>
    public override int SaveChanges()
    {
        return _runtime.SaveChanges(base.SaveChanges, acceptAllChangesOnSuccess: true);
    }

    /// <summary>
    /// Runs the Headless save pipeline and persists changes, controlling whether EF Core calls
    /// <c>AcceptAllChanges</c> on success.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">
    /// Indicates whether EF Core should call <c>AcceptAllChanges</c> after a successful save.
    /// Pass <see langword="false"/> when you manage change-tracking resets yourself (for example in a
    /// two-phase save pattern).
    /// </param>
    /// <returns>The number of state entries written to the database.</returns>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        return _runtime.SaveChanges(base.SaveChanges, acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Asynchronously runs the Headless save pipeline and persists all changes to the database.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _runtime.SaveChangesAsync(base.SaveChangesAsync, acceptAllChangesOnSuccess: true, cancellationToken);
    }

    /// <summary>
    /// Asynchronously runs the Headless save pipeline and persists changes, controlling whether EF Core
    /// calls <c>AcceptAllChanges</c> on success.
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess">
    /// Indicates whether EF Core should call <c>AcceptAllChanges</c> after a successful save.
    /// </param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        return _runtime.SaveChangesAsync(base.SaveChangesAsync, acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Releases the scope bound to this context, then disposes the base EF Core context, which returns a pooled
    /// instance to its pool, then disposes the scope the context owned, if any. A pooled context resolved from a
    /// scope is returned when the scope ends, so disposing it earlier does nothing.
    /// </summary>
    public override void Dispose()
    {
        if (_runtime.IsScopeLeased)
        {
            return;
        }

        var ownedScope = _runtime.Release();

        try
        {
            base.Dispose();
        }
        finally
        {
            HeadlessDbContextRuntime.DisposeScope(ownedScope, GetType());
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Asynchronously releases the scope bound to this context, disposes the base EF Core context, which returns a
    /// pooled instance to its pool, then disposes the scope the context owned, if any.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (_runtime.IsScopeLeased)
        {
            return;
        }

        var ownedScope = _runtime.Release();

        try
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await HeadlessDbContextRuntime.DisposeScopeAsync(ownedScope, GetType()).ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Applies Headless primitive-type value converter mappings in addition to any conventions the
    /// subclass registers. Always call <c>base.ConfigureConventions</c> when overriding.
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        _runtime.ConfigureConventions(configurationBuilder);
    }

    /// <summary>
    /// Applies the <see cref="DefaultSchema"/> (if non-null), calls <c>base.OnModelCreating</c>, and then
    /// lets the Headless runtime apply global query filters (multi-tenancy, soft-delete, suspend) and
    /// entity conventions. Always call <c>base.OnModelCreating</c> when overriding.
    /// </summary>
    /// <param name="modelBuilder">The model builder for the current context.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!string.IsNullOrWhiteSpace(DefaultSchema))
        {
            modelBuilder.HasDefaultSchema(DefaultSchema);
        }

        base.OnModelCreating(modelBuilder);
        HeadlessDbContextRuntime.ProcessModelCreating(modelBuilder);
    }
}

internal static partial class HeadlessDbContextLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessDbContextOwnedScopeDisposeFailed",
        Level = LogLevel.Warning,
        Message = "Owned service-scope disposal failed; the primary disposal exception (if any) takes precedence and is rethrown to the caller."
    )]
    public static partial void LogOwnedScopeDisposeFailed(this ILogger logger, Exception exception);
}
