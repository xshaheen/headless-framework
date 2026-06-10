// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Internal contract shared by the Headless DbContext bases (<see cref="HeadlessDbContext"/> and the Identity
/// context) so the runtime, save pipeline, factory, and disposal infrastructure operate against either base
/// without a common class — the Identity context must derive from <c>IdentityDbContext</c>, so a marker
/// interface is the only shared seam. Exposes the tenant/schema the runtime reads and the per-call service
/// scope the factory hands in.
/// </summary>
internal interface IHeadlessDbContext
{
    string? DefaultSchema { get; }

    string? TenantId { get; }

    /// <summary>
    /// The scoped (request) service provider that resolved this context — used by coordinated-transaction
    /// helpers to enlist with the correct scope for the post-commit drain.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Optional service scope owned by the context — set by <c>HeadlessDbContextFactory</c> when the context
    /// is created via <c>IDbContextFactory&lt;TDbContext&gt;</c>, and disposed with the context (see
    /// <see cref="HeadlessDbContextDisposal"/>).
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
public abstract class HeadlessDbContext : DbContext, IHeadlessDbContext
{
    private readonly HeadlessDbContextRuntime _runtime;

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

    public abstract string? DefaultSchema { get; }

    public string? TenantId => _runtime.TenantId;

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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        _runtime.ConfigureConventions(configurationBuilder);
    }

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
