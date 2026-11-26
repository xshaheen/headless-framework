// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class CustomProcessorTenantWriteGuardTests : TestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task should_reject_a_tenant_change_made_by_a_custom_save_processor(bool sync, bool useTransaction)
    {
        // given
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        builder.Services.AddHeadlessDbContext<TenantDbContext>(
            options => options.UseSqlite(connection),
            options => options.AddSaveEntryProcessor<ReassignTenantProcessor>(ServiceLifetime.Singleton)
        );

        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var tenantScope = provider.GetRequiredService<ICurrentTenant>().Change("tenant-a");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await db.Database.EnsureCreatedAsync(AbortToken);
        var row = new TenantRow
        {
            Id = Guid.NewGuid(),
            Owner = "tenant-a",
            Name = "original",
        };
        db.Add(row);
        await db.SaveChangesAsync(AbortToken);

        // when
        await using var transaction = useTransaction ? await db.Database.BeginTransactionAsync(AbortToken) : null;
        row.Name = "updated";
        var save = () => sync ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(AbortToken);

        // then
        await save.Should().ThrowAsync<CrossTenantWriteException>();
        var persisted = await db.Set<TenantRow>().AsNoTracking().SingleAsync(AbortToken);
        persisted.Owner.Should().Be("tenant-a");
        persisted.Name.Should().Be("original");
    }

    private sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantRow>().IsTenantOwned(nameof(TenantRow.Owner));
        }
    }

    private sealed class TenantRow
    {
        public Guid Id { get; init; }

        public string Owner { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ReassignTenantProcessor : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
        {
            if (entry.State == EntityState.Modified && entry.Entity is TenantRow row)
            {
                row.Owner = "tenant-b";
            }
        }
    }
}
