// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework.Contexts;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Entities;
using Tests.Fixtures;

namespace Tests;

/// <summary>
/// Abstract base class for testing HeadlessDbContext global query filter behavior.
/// Tests multi-tenancy, soft-delete, and suspend filters.
/// </summary>
/// <typeparam name="TFixture">The fixture type providing database infrastructure.</typeparam>
/// <typeparam name="TContext">The DbContext type implementing IHarnessDbContext.</typeparam>
public abstract class HeadlessDbContextGlobalFiltersTestBase<TFixture, TContext> : TestBase
    where TFixture : class, IDbContextTestFixture<TContext>
    where TContext : DbContext, IHarnessDbContext
{
    protected readonly TFixture Fixture;

    protected HeadlessDbContextGlobalFiltersTestBase(TFixture fixture)
    {
        Fixture = fixture;
        using var scope = Fixture.ServiceProvider.CreateScope();
        scope.ServiceProvider.EnsureDbRecreated<TContext>();
    }

    // Note: This test demonstrates EF Core query plan caching. The tenant filter works
    // correctly with proper parameterization when contexts are created fresh per tenant.
    // See HeadlessDbContextTests.global_filters_should_filter_by_tenant_delete_and_suspend_flags_and_can_be_disabled
    // for the reference implementation that works with the same context.
    [Fact(Skip = "Requires context-per-tenant pattern - see HeadlessDbContextTests for working example")]
    public virtual async Task global_filters_should_filter_by_tenant_id()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var t1 = new HarnessTestEntity { Name = "tenant-1-entity", TenantId = "TENANT-1" };
        var t2 = new HarnessTestEntity { Name = "tenant-2-entity", TenantId = "TENANT-2" };
        await db.TestEntities.AddRangeAsync(t1, t2);
        await db.SaveChangesAsync(AbortToken);

        // when/then
        using (Fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.TestEntities.Select(x => x.Name).ToListAsync(AbortToken);
            items.Should().BeEquivalentTo("tenant-1-entity");
        }

        using (Fixture.CurrentTenant.Change("TENANT-2"))
        {
            var items = await db.TestEntities.Select(x => x.Name).ToListAsync(AbortToken);
            items.Should().BeEquivalentTo("tenant-2-entity");
        }
    }

    [Fact]
    public virtual async Task global_filters_should_filter_deleted_entities()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var active = new HarnessTestEntity { Name = "active", TenantId = "TENANT-1" };
        var deleted = new HarnessTestEntity { Name = "deleted", TenantId = "TENANT-1" };
        await db.TestEntities.AddRangeAsync(active, deleted);
        await db.SaveChangesAsync(AbortToken);

        deleted.MarkDeleted();
        db.Update(deleted);
        await db.SaveChangesAsync(AbortToken);

        // when/then
        using (Fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.TestEntities.Select(x => x.Name).ToListAsync(AbortToken);
            items.Should().BeEquivalentTo("active");
        }
    }

    [Fact]
    public virtual async Task global_filters_should_filter_suspended_entities()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var active = new HarnessTestEntity { Name = "active", TenantId = "TENANT-1" };
        var suspended = new HarnessTestEntity { Name = "suspended", TenantId = "TENANT-1" };
        await db.TestEntities.AddRangeAsync(active, suspended);
        await db.SaveChangesAsync(AbortToken);

        suspended.MarkSuspended();
        db.Update(suspended);
        await db.SaveChangesAsync(AbortToken);

        // when/then
        using (Fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.TestEntities.Select(x => x.Name).ToListAsync(AbortToken);
            items.Should().BeEquivalentTo("active");
        }
    }

    [Fact]
    public virtual async Task should_ignore_multi_tenancy_filter()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var t1 = new HarnessTestEntity { Name = "tenant-1", TenantId = "TENANT-1" };
        var t2 = new HarnessTestEntity { Name = "tenant-2", TenantId = "TENANT-2" };
        var noTenant = new HarnessTestEntity { Name = "no-tenant", TenantId = null };
        await db.TestEntities.AddRangeAsync(t1, t2, noTenant);
        await db.SaveChangesAsync(AbortToken);

        // when/then - ignoring multi-tenancy should show all non-deleted/non-suspended entities
        using (Fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.TestEntities.IgnoreMultiTenancyFilter().Select(x => x.Name).ToListAsync(AbortToken);

            items.Should().BeEquivalentTo("tenant-1", "tenant-2", "no-tenant");
        }
    }

    [Fact]
    public virtual async Task should_ignore_not_deleted_filter()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var active = new HarnessTestEntity { Name = "active", TenantId = "TENANT-1" };
        var deleted = new HarnessTestEntity { Name = "deleted", TenantId = "TENANT-1" };
        await db.TestEntities.AddRangeAsync(active, deleted);
        await db.SaveChangesAsync(AbortToken);

        deleted.MarkDeleted();
        db.Update(deleted);
        await db.SaveChangesAsync(AbortToken);

        // when/then - ignoring deleted filter should show both active and deleted
        using (Fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.TestEntities.IgnoreNotDeletedFilter().Select(x => x.Name).ToListAsync(AbortToken);

            items.Should().BeEquivalentTo("active", "deleted");
        }
    }

    [Fact]
    public virtual async Task should_ignore_not_suspended_filter()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var active = new HarnessTestEntity { Name = "active", TenantId = "TENANT-1" };
        var suspended = new HarnessTestEntity { Name = "suspended", TenantId = "TENANT-1" };
        await db.TestEntities.AddRangeAsync(active, suspended);
        await db.SaveChangesAsync(AbortToken);

        suspended.MarkSuspended();
        db.Update(suspended);
        await db.SaveChangesAsync(AbortToken);

        // when/then - ignoring suspended filter should show both active and suspended
        using (Fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.TestEntities.IgnoreNotSuspendedFilter().Select(x => x.Name).ToListAsync(AbortToken);

            items.Should().BeEquivalentTo("active", "suspended");
        }
    }

    [Fact]
    public virtual async Task should_combine_multiple_filter_ignores()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var activeT1 = new HarnessTestEntity { Name = "active-t1", TenantId = "TENANT-1" };
        var activeT2 = new HarnessTestEntity { Name = "active-t2", TenantId = "TENANT-2" };
        var deletedT1 = new HarnessTestEntity { Name = "deleted-t1", TenantId = "TENANT-1" };
        var suspendedT1 = new HarnessTestEntity { Name = "suspended-t1", TenantId = "TENANT-1" };
        var noTenant = new HarnessTestEntity { Name = "no-tenant", TenantId = null };
        await db.TestEntities.AddRangeAsync(activeT1, activeT2, deletedT1, suspendedT1, noTenant);
        await db.SaveChangesAsync(AbortToken);

        deletedT1.MarkDeleted();
        suspendedT1.MarkSuspended();
        db.UpdateRange(deletedT1, suspendedT1);
        await db.SaveChangesAsync(AbortToken);

        // when/then - disabling all filters should show all entities
        var items = await db
            .TestEntities.IgnoreMultiTenancyFilter()
            .IgnoreNotDeletedFilter()
            .IgnoreNotSuspendedFilter()
            .Select(x => x.Name)
            .ToListAsync(AbortToken);

        items.Should().BeEquivalentTo("active-t1", "active-t2", "deleted-t1", "suspended-t1", "no-tenant");
    }
}
