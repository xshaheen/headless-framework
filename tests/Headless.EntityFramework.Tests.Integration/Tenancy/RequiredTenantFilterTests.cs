// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tests.Fixture;

namespace Tests.Tenancy;

/// <summary>
/// With the read guard off, the tenant filter on a required tenant column compares the column to the ambient tenant
/// with no separate null check, so on a real relational database a query without a tenant must match no row rather
/// than every row, on PostgreSQL and on SQLite.
/// </summary>
[Collection<HeadlessDbContextTestFixture>]
public sealed class RequiredTenantFilterTests(HeadlessDbContextTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_return_no_rows_from_a_required_tenant_column_when_no_tenant_is_set()
    {
        // given: one row stored for a tenant, in a database of this test's own
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.SqlConnectionString)
        {
            Database = $"required_tenant_{Guid.NewGuid():N}",
        }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<RequiredTenantDbContext>(options => options.UseNpgsql(connectionString));
        await using var provider = services.BuildServiceProvider();
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RequiredTenantDbContext>();
            await db.Database.EnsureCreatedAsync(AbortToken);

            // The write guard is off, so nothing stamps the shadow tenant column.
            var row = db.Add(new RequiredTenantRow { Id = Guid.NewGuid() });
            row.Property("TenantId").CurrentValue = "tenant-a";
            await db.SaveChangesAsync(AbortToken);
        }

        try
        {
            // when
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RequiredTenantDbContext>();
            var filtered = await db.Set<RequiredTenantRow>().CountAsync(AbortToken);
            var stored = await db.Set<RequiredTenantRow>().IgnoreMultiTenancyFilter().CountAsync(AbortToken);

            // then
            currentTenant.Id.Should().BeNull();
            filtered.Should().Be(0);
            stored.Should().Be(1);
        }
        finally
        {
            await using var scope = provider.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<RequiredTenantDbContext>()
                .Database.EnsureDeletedAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_return_no_rows_from_a_required_tenant_column_when_no_tenant_is_set_on_sqlite()
    {
        // given: one row stored for a tenant, in an in-memory SQLite database held open by this connection
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<RequiredTenantDbContext>(options => options.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RequiredTenantDbContext>();
            await db.Database.EnsureCreatedAsync(AbortToken);

            var row = db.Add(new RequiredTenantRow { Id = Guid.NewGuid() });
            row.Property("TenantId").CurrentValue = "tenant-a";
            await db.SaveChangesAsync(AbortToken);
        }

        // when
        await using var readScope = provider.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<RequiredTenantDbContext>();
        var filtered = await reader.Set<RequiredTenantRow>().CountAsync(AbortToken);
        var stored = await reader.Set<RequiredTenantRow>().IgnoreMultiTenancyFilter().CountAsync(AbortToken);

        // then
        currentTenant.Id.Should().BeNull();
        filtered.Should().Be(0);
        stored.Should().Be(1);
    }

    private sealed class RequiredTenantDbContext(DbContextOptions<RequiredTenantDbContext> options)
        : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Tenant-owned without IMultiTenant, so the tenant column is required.
            modelBuilder.Entity<RequiredTenantRow>().IsTenantOwned();
        }
    }

    private sealed class RequiredTenantRow : Entity<Guid>;
}
