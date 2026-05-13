// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

public interface IHeadlessDbContext
{
    string? DefaultSchema { get; }

    string? TenantId { get; }
}

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
