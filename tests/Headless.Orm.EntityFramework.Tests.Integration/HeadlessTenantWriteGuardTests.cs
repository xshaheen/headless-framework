using Headless.EntityFramework;
using Headless.EntityFramework.MultiTenancy;
using Headless.Abstractions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Fixture;

namespace Tests;

public sealed class HeadlessTenantWriteGuardTests : TestBase
{
    [Fact]
    public void cross_tenant_write_exception_should_expose_safe_structural_diagnostics()
    {
        // given
        var exception = new CrossTenantWriteException(
            entityType: typeof(TestEntity).FullName!,
            writeState: "Modified",
            currentTenantAvailable: true,
            entityTenantAvailable: true,
            tenantMatches: false
        );

        // then
        exception.EntityType.Should().Be(typeof(TestEntity).FullName);
        exception.FailureCategory.Should().Be("CrossTenantWrite");
        exception.WriteState.Should().Be("Modified");
        exception.CurrentTenantAvailable.Should().BeTrue();
        exception.EntityTenantAvailable.Should().BeTrue();
        exception.TenantMatches.Should().BeFalse();

        exception.Data["EntityType"].Should().Be(typeof(TestEntity).FullName);
        exception.Data["FailureCategory"].Should().Be("CrossTenantWrite");
        exception.Data["WriteState"].Should().Be("Modified");
        exception.Data["CurrentTenantAvailable"].Should().Be(true);
        exception.Data["EntityTenantAvailable"].Should().Be(true);
        exception.Data["TenantMatches"].Should().Be(false);

        exception.Message.Should().Contain(nameof(TestEntity));
        exception.Message.Should().Contain("Modified");
        exception.Message.Should().NotContain("tenant-a");
        exception.Message.Should().NotContain("tenant-b");
        exception.Data.Values.Cast<object?>().Should().NotContain("tenant-a").And.NotContain("tenant-b");
    }

    [Fact]
    public void add_headless_db_context_services_should_register_disabled_tenant_write_guard_options()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessDbContextServices();

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeFalse();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void add_headless_tenant_write_guard_should_enable_options()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessTenantWriteGuard();

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeTrue();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void tenant_write_guard_bypass_should_restore_after_dispose()
    {
        // given
        var bypass = new TenantWriteGuardBypass();

        // when
        using (bypass.BeginBypass())
        {
            // then
            bypass.IsActive.Should().BeTrue();
        }

        bypass.IsActive.Should().BeFalse();
    }

    [Fact]
    public void tenant_write_guard_bypass_should_restore_nested_scopes_in_lifo_order()
    {
        // given
        var bypass = new TenantWriteGuardBypass();

        // when
        using (bypass.BeginBypass())
        {
            bypass.IsActive.Should().BeTrue();

            using (bypass.BeginBypass())
            {
                bypass.IsActive.Should().BeTrue();
            }

            // then
            bypass.IsActive.Should().BeTrue();
        }

        bypass.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task tenant_write_guard_bypass_should_not_leak_into_unrelated_async_flow()
    {
        // given
        var bypass = new TenantWriteGuardBypass();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var unrelated = Task.Run(async () =>
        {
            await release.Task;

            return bypass.IsActive;
        });

        // when
        using (bypass.BeginBypass())
        {
            bypass.IsActive.Should().BeTrue();
        }

        release.SetResult();

        // then
        var leaked = await unrelated;
        leaked.Should().BeFalse();
        bypass.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task guard_disabled_should_preserve_current_create_without_current_tenant_behavior()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: false);
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "unguarded" };
        db.Tests.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.TenantId.Should().BeNull();
        var persisted = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(AbortToken);
        persisted.Name.Should().Be("unguarded");
        persisted.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task guard_enabled_should_reject_tenant_owned_add_without_current_tenant_before_side_effects()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "missing-tenant" };
        db.Tests.Add(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<MissingTenantContextException>();
        db.EmittedLocalMessages.Should().BeEmpty();
        db.EmittedDistributedMessages.Should().BeEmpty();
        var persisted = await db.Tests.IgnoreMultiTenancyFilter().CountAsync(AbortToken);
        persisted.Should().Be(0);
    }

    [Fact]
    public async Task guard_enabled_should_stamp_added_tenant_owned_entity_under_current_tenant()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "tenant-owned" };
        db.Tests.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.TenantId.Should().Be("tenant-a");
        var persistedTenant = await db
            .Tests.IgnoreMultiTenancyFilter()
            .Where(x => x.Id == entity.Id)
            .Select(x => x.TenantId)
            .SingleAsync(AbortToken);
        persistedTenant.Should().Be("tenant-a");
    }

    [Fact]
    public async Task guard_enabled_should_reject_tenant_owned_add_with_different_tenant()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        db.Tests.Add(new TestEntity { Name = "wrong-tenant", TenantId = "tenant-b" });

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        var exception = await act.Should().ThrowAsync<CrossTenantWriteException>();
        exception.Which.EntityType.Should().Be(typeof(TestEntity).FullName);
        exception.Which.WriteState.Should().Be(nameof(EntityState.Added));
        exception.Which.CurrentTenantAvailable.Should().BeTrue();
        exception.Which.EntityTenantAvailable.Should().BeTrue();
        exception.Which.TenantMatches.Should().BeFalse();
        db.EmittedLocalMessages.Should().BeEmpty();
        var persisted = await db.Tests.IgnoreMultiTenancyFilter().CountAsync(AbortToken);
        persisted.Should().Be(0);
    }

    [Fact]
    public async Task guard_enabled_should_allow_matching_tenant_update_and_physical_delete()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "initial" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();

        // when
        var saved = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(AbortToken);
        saved.Name = "updated";
        await db.SaveChangesAsync(AbortToken);

        db.Tests.Remove(saved);
        await db.SaveChangesAsync(AbortToken);

        // then
        var remaining = await db.Tests.IgnoreMultiTenancyFilter().CountAsync(AbortToken);
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task guard_enabled_should_reject_cross_tenant_update_loaded_through_ignored_filter()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-b", "owned-by-b");

        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        entity.Name = "changed-by-a";

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persistedName = await _GetTenantEntityNameAsync(fixture, entityId);
        persistedName.Should().Be("owned-by-b");
    }

    [Fact]
    public async Task guard_enabled_should_reject_cross_tenant_physical_delete_loaded_through_ignored_filter()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-b", "delete-owned-by-b");

        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        db.Tests.Remove(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persisted = await _TenantEntityExistsAsync(fixture, entityId);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task guard_enabled_should_reject_cross_tenant_soft_delete_as_modified_write()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-b", "soft-delete-owned-by-b");

        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        entity.MarkDeleted();
        db.Update(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persisted = await _GetTenantEntityIsDeletedAsync(fixture, entityId);
        persisted.Should().BeFalse();
    }

    [Fact]
    public async Task guard_enabled_should_allow_non_tenant_entity_add_update_and_delete_without_current_tenant()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new BasicEntity { Name = "basic" };
        db.Basics.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        db.Entry(entity).Property(nameof(BasicEntity.Name)).CurrentValue = "basic-updated";
        await db.SaveChangesAsync(AbortToken);

        db.Basics.Remove(entity);
        await db.SaveChangesAsync(AbortToken);

        // then
        var remaining = await db.Basics.CountAsync(AbortToken);
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task guard_enabled_should_allow_scoped_bypass_and_restore_strict_behavior_after_dispose()
    {
        // given
        await using var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled: true);
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();
        var bypass = scope.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>();

        // when
        using (bypass.BeginBypass())
        {
            db.Tests.Add(new TestEntity { Name = "host-write" });
            await db.SaveChangesAsync(AbortToken);
        }

        db.ChangeTracker.Clear();
        var seededId = await _SeedTenantEntityAsync(fixture, "tenant-b", "bypass-owned-by-b");
        fixture.CurrentTenant.Id = "tenant-a";

        using (bypass.BeginBypass())
        {
            var crossTenant = await db
                .Tests.IgnoreMultiTenancyFilter()
                .SingleAsync(x => x.Id == seededId, AbortToken);
            crossTenant.Name = "bypass-updated";
            await db.SaveChangesAsync(AbortToken);
        }

        db.Tests.Add(new TestEntity { Name = "strict-again", TenantId = "tenant-b" });

        // then
        var act = async () => await db.SaveChangesAsync(AbortToken);
        await act.Should().ThrowAsync<CrossTenantWriteException>();

        var hostWriteCount = await db.Tests.IgnoreMultiTenancyFilter().CountAsync(x => x.Name == "host-write", AbortToken);
        hostWriteCount.Should().Be(1);
        var bypassName = await _GetTenantEntityNameAsync(fixture, seededId);
        bypassName.Should().Be("bypass-updated");
    }

    private async Task<Guid> _SeedTenantEntityAsync(
        TenantWriteGuardDbContextTestFixture fixture,
        string tenantId,
        string name
    )
    {
        using var tenant = fixture.CurrentTenant.Change(tenantId);
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = name };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        return entity.Id;
    }

    private async Task<string> _GetTenantEntityNameAsync(TenantWriteGuardDbContextTestFixture fixture, Guid entityId)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db
            .Tests.IgnoreMultiTenancyFilter()
            .Where(x => x.Id == entityId)
            .Select(x => x.Name)
            .SingleAsync(AbortToken);
    }

    private async Task<bool> _GetTenantEntityIsDeletedAsync(TenantWriteGuardDbContextTestFixture fixture, Guid entityId)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db
            .Tests.IgnoreMultiTenancyFilter()
            .IgnoreNotDeletedFilter()
            .Where(x => x.Id == entityId)
            .Select(x => x.IsDeleted)
            .SingleAsync(AbortToken);
    }

    private async Task<bool> _TenantEntityExistsAsync(TenantWriteGuardDbContextTestFixture fixture, Guid entityId)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db.Tests.IgnoreMultiTenancyFilter().AnyAsync(x => x.Id == entityId, AbortToken);
    }
}
