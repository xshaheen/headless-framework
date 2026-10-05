// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The tenant query filter reads the executing context's tenant as a query parameter, so one compiled plan serves
/// every tenant. If the tenant were baked into the plan, the second tenant to run a query would read the rows of the
/// first.
/// </summary>
public sealed class TenantQueryPlanSharingTests : TestBase
{
    [Fact]
    public async Task should_filter_each_tenant_when_tenants_run_the_same_query()
    {
        // given — one row per tenant
        await using var keeper = new SqliteConnection("Data Source=tenant-query-plan;Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(AbortToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<TenantPlanDbContext>(options => options.UseSqlite(keeper.ConnectionString));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var currentTenant = provider.GetRequiredService<ICurrentTenant>();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantPlanDbContext>();
            await db.Database.EnsureCreatedAsync(AbortToken);

            foreach (var tenant in new[] { "tenant-a", "tenant-b" })
            {
                using (currentTenant.Change(tenant))
                {
                    db.Add(new TenantPlanRow { Id = Guid.NewGuid() });
                    await db.SaveChangesAsync(AbortToken);
                }
            }
        }

        // when — the same query shape, run first for one tenant and then for the other
        var tenantARows = await _ReadAsync(provider, currentTenant, "tenant-a");
        var tenantBRows = await _ReadAsync(provider, currentTenant, "tenant-b");

        // then
        tenantARows.Should().ContainSingle().Which.Should().Be("tenant-a");
        tenantBRows.Should().ContainSingle().Which.Should().Be("tenant-b");
    }

    private async Task<List<string?>> _ReadAsync(IServiceProvider provider, ICurrentTenant currentTenant, string tenant)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantPlanDbContext>();

        using (currentTenant.Change(tenant))
        {
            return await db.Set<TenantPlanRow>().OrderBy(x => x.Id).Select(x => x.TenantId).ToListAsync(AbortToken);
        }
    }

    private sealed class TenantPlanDbContext(DbContextOptions<TenantPlanDbContext> options) : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantPlanRow>().HasKey(x => x.Id);
        }
    }

    private sealed class TenantPlanRow : IMultiTenant
    {
        public Guid Id { get; set; }

        public string? TenantId { get; set; }
    }
}
