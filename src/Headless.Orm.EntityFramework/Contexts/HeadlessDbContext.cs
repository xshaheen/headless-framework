// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Capability seam shared by the Headless DbContext bases (<see cref="HeadlessDbContext"/> and the Identity
/// context) so the runtime, save pipeline, factory, disposal infrastructure, and coordinated-transaction
/// helpers operate against either base without a common class — the Identity context must derive from
/// <c>IdentityDbContext</c>, so this interface is the only shared seam. Exposes the tenant/schema the runtime
/// reads and the per-call service scope the factory hands in. Implemented explicitly by both bases, so it does
/// not widen their public surface; it is public so capability-based extensions (e.g.
/// <c>ExecuteCoordinatedTransactionAsync</c>) can target any Headless-managed context.
/// </summary>
[PublicAPI]
public interface IHeadlessDbContext
{
    /// <summary>
    /// Optional database schema applied to all entities that do not declare an explicit schema.
    /// <see langword="null"/> leaves the provider default in place.
    /// </summary>
    string? DefaultSchema { get; }

    /// <summary>
    /// The identifier of the tenant whose data this context is scoped to, or <see langword="null"/>
    /// when running in a host/admin context outside a tenant scope.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// The scoped (request) service provider that resolved this context — used by coordinated-transaction
    /// helpers to enlist with the correct scope for the post-commit drain.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
}

/// <summary>
/// Internal lifecycle seam for the service scope a factory-created context owns. Kept off the public
/// <see cref="IHeadlessDbContext"/> entirely: only <c>HeadlessDbContextFactory</c> (set) and
/// <c>HeadlessDbContextDisposal</c> (get) touch it, so consumers neither read nor reassign it.
/// </summary>
internal interface IHeadlessDbContextScopeOwner
{
    /// <summary>
    /// Optional service scope owned by the context — set by <c>HeadlessDbContextFactory</c> when the context is
    /// created via <c>IDbContextFactory&lt;TDbContext&gt;</c>, and disposed with the context.
    /// </summary>
    IServiceScope? OwnedScope { get; set; }
}

/// <summary>
/// Base <see cref="DbContext"/> with the framework's save pipeline, multi-tenancy filter,
/// auditing, soft-delete, and domain-event dispatch wired in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not poolable.</b> Do not register subclasses with <c>AddDbContextPool</c> or
/// <c>AddPooledDbContextFactory</c>. Two independent reasons:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// The instance holds a private <c>HeadlessDbContextRuntime</c> field that captures the
/// scoped save pipeline (outbox dispatcher, audit persistence). Pooled instances would
/// reuse a prior request's unit of work — a captive-dependency correctness bug. EF's own
/// guidance: avoid pooling when the context maintains private state, since EF only resets
/// state it is aware of.
/// </description>
/// </item>
/// <item>
/// <description>
/// The constructor takes a second non-<see cref="DbContextOptions"/> parameter
/// (<see cref="HeadlessDbContextServices"/>); the pooling path resolves contexts through a
/// single-<see cref="DbContextOptions"/> constructor and does not support this shape.
/// </description>
/// </item>
/// </list>
/// <para>
/// <c>ICurrentTenant</c> (AsyncLocal-backed) and <c>IClock</c> are not the blocker — only
/// the save/outbox/audit pipeline is request-bound. Use a plain <see cref="DbContext"/>
/// with <c>AddDbContextPool</c> for read-heavy hot paths that do not need the Headless
/// write machinery.
/// </para>
/// </remarks>
public abstract class HeadlessDbContext : DbContext, IHeadlessDbContext, IHeadlessDbContextScopeOwner
{
    private readonly HeadlessDbContextRuntime _runtime;

    /// <summary>
    /// Initializes the context with the Headless infrastructure services and the EF Core options.
    /// </summary>
    /// <param name="services">
    /// The Headless EF Core services bundle resolved from DI. Resolved automatically when the context is
    /// registered with <c>AddHeadlessDbContext</c>.
    /// </param>
    /// <param name="options">The EF Core options for this context type.</param>
    protected HeadlessDbContext(HeadlessDbContextServices services, DbContextOptions options)
        : base(options)
    {
        _runtime = new(this, services);
        _runtime.Initialize();
    }

    // Optional service scope owned by this context — set by HeadlessDbContextFactory (via the
    // IHeadlessDbContext seam) when the context is created through IDbContextFactory<TDbContext>.
    // Disposed alongside the context so factory-created contexts don't leak per-call scopes.
    private IServiceScope? _ownedScope;

    /// <summary>
    /// Returns the optional default database schema applied to entities that do not declare their own.
    /// Override in subclasses to set a schema (for example return <c>"myapp"</c>); return
    /// <see langword="null"/> to use the provider default.
    /// </summary>
    public abstract string? DefaultSchema { get; }

    /// <summary>
    /// Returns the tenant identifier captured from the ambient <c>ICurrentTenant</c> when this instance
    /// was created. The multi-tenancy global query filter and the write guard both read this value.
    /// </summary>
    public string? TenantId => _runtime.TenantId;

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
    /// Disposes the Headless runtime and the base EF Core context, then disposes any owned service
    /// scope created by <c>IDbContextFactory</c>.
    /// </summary>
    public override void Dispose()
    {
        // Drain the runtime (sync today — ValueTask.CompletedTask) then the base context. try/finally
        // guarantees the owned scope still disposes if either throws. Owned-scope disposal is centralized
        // in HeadlessDbContextDisposal so it stays identical with the Identity context base.
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
    /// Asynchronously disposes the Headless runtime and the base EF Core context, then disposes any
    /// owned service scope created by <c>IDbContextFactory</c>.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        // Detach the runtime's ChangeTracker handlers before the base context tears down its services —
        // avoids EF "still tracking" assertions and handler leaks across repeated resolves.
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
    /// Applies Headless primitive-type value converter mappings in addition to any conventions the
    /// subclass registers. Always call <c>base.ConfigureConventions</c> when overriding.
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        HeadlessDbContextRuntime.ConfigureConventions(configurationBuilder);
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
        _runtime.ProcessModelCreating(modelBuilder);
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
