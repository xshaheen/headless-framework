// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

public interface IHeadlessDbContext
{
    string? DefaultSchema { get; }

    string? TenantId { get; }
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

    public abstract string? DefaultSchema { get; }

    public string? TenantId => _runtime.TenantId;

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
        // Synchronously dispose the runtime alongside the base context. DisposeAsync's body is
        // synchronous (ValueTask.CompletedTask), so the sync path can call DisposeAsync().AsTask()
        // safely without blocking. We keep the dispose contract symmetrical with DisposeAsync.
        var disposeTask = _runtime.DisposeAsync();
        if (!disposeTask.IsCompletedSuccessfully)
        {
            disposeTask.AsTask().GetAwaiter().GetResult();
        }
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public override async ValueTask DisposeAsync()
    {
        // Detach the per-DbContext runtime's ChangeTracker handlers BEFORE we let the base context
        // tear down its services. Avoids EF's "still tracking" assertions and prevents handler leaks
        // when the same DbContext type is resolved repeatedly under the same service provider.
        await _runtime.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
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
