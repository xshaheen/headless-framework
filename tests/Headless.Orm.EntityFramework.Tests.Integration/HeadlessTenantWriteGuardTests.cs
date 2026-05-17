// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tests.Fixture;

namespace Tests;

[Collection<TenantWriteGuardCollection>]
public sealed class HeadlessTenantWriteGuardTests : TestBase
{
    private readonly TenantWriteGuardEnabledFixture _enabledFixture;
    private readonly TenantWriteGuardDisabledFixture _disabledFixture;

    public HeadlessTenantWriteGuardTests(
        TenantWriteGuardEnabledFixture enabledFixture,
        TenantWriteGuardDisabledFixture disabledFixture
    )
    {
        _enabledFixture = enabledFixture;
        _disabledFixture = disabledFixture;
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Task.WhenAll(_enabledFixture.ResetAsync(), _disabledFixture.ResetAsync());
    }

    [Fact]
    public void guard_tenant_writes_capabilities_should_expose_documented_capability_labels()
    {
        // given/when — the static capabilities property must contain the two labels recorded by the
        // EF seam so downstream posture assertions stay stable across refactors of the seam wiring.
        var capabilities = HeadlessEntityFrameworkTenancyBuilder.GuardTenantWritesCapabilities;

        // then
        capabilities.Should().BeEquivalentTo("guard-tenant-writes", "ef-owned-bypass");
    }

    [Fact]
    public void cross_tenant_write_exception_should_expose_safe_structural_diagnostics()
    {
        // given
        var exception = new CrossTenantWriteException(entityType: typeof(TestEntity).FullName!, operation: "Modified");

        // then
        exception.EntityType.Should().Be(typeof(TestEntity).FullName);
        exception.Operation.Should().Be("Modified");
        CrossTenantWriteException.FailureCategoryName.Should().Be("CrossTenantWrite");

        exception.Message.Should().Contain(nameof(TestEntity));
        exception.Message.Should().Contain("Modified");
        exception.Message.Should().NotContain("tenant-a");
        exception.Message.Should().NotContain("tenant-b");
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
    public void add_headless_tenant_write_guard_with_noop_configurator_should_keep_guard_enabled()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessTenantWriteGuard(_ => { });

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeTrue();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void add_headless_tenant_write_guard_should_bind_configuration_and_keep_guard_enabled()
    {
        // given
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("IsEnabled", "false")])
            .Build();

        // when
        services.AddHeadlessTenantWriteGuard(configuration);

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeTrue();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void add_headless_tenant_write_guard_should_support_service_provider_aware_configuration()
    {
        // given
        var marker = new TenantWriteGuardOptionsMarker(Guid.NewGuid());
        Guid? resolvedMarkerId = null;
        var services = new ServiceCollection();
        services.AddSingleton(marker);

        // when
        services.AddHeadlessTenantWriteGuard(
            (_, provider) =>
            {
                resolvedMarkerId = provider.GetRequiredService<TenantWriteGuardOptionsMarker>().Id;
            }
        );

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;

        // then
        options.IsEnabled.Should().BeTrue();
        resolvedMarkerId.Should().Be(marker.Id);
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void add_headless_tenancy_entity_framework_should_enable_write_guard_and_record_manifest()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));

        using var provider = builder.Services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeTrue();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();

        var manifest = builder.Services.GetOrAddTenantPostureManifest();
        var seam = manifest.GetSeam("EntityFramework");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Guarded);
        seam.Capabilities.Should().BeEquivalentTo("guard-tenant-writes", "ef-owned-bypass");
    }

    [Fact]
    public void guard_tenant_writes_with_noop_configurator_should_keep_guard_enabled()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites(_ => { })));

        using var provider = builder.Services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeTrue();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void tenant_write_guard_bypass_should_restore_after_dispose()
    {
        // given
        using var provider = _BuildBypassProvider();
        var bypass = provider.GetRequiredService<ITenantWriteGuardBypass>();

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
        using var provider = _BuildBypassProvider();
        var bypass = provider.GetRequiredService<ITenantWriteGuardBypass>();

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
        using var provider = _BuildBypassProvider();
        var bypass = provider.GetRequiredService<ITenantWriteGuardBypass>();
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
    public async Task tenant_write_guard_bypass_should_not_leak_into_async_work_spawned_inside_scope()
    {
        // given
        using var provider = _BuildBypassProvider();
        var bypass = provider.GetRequiredService<ITenantWriteGuardBypass>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> captured;

        // when
        using (bypass.BeginBypass())
        {
            captured = Task.Run(async () =>
            {
                started.SetResult();
                await release.Task;

                return bypass.IsActive;
            });

            await started.Task;
            bypass.IsActive.Should().BeTrue();
        }

        release.SetResult();

        // then
        var leaked = await captured;
        leaked.Should().BeFalse();
        bypass.IsActive.Should().BeFalse();
    }

    private static ServiceProvider _BuildBypassProvider()
    {
        var services = new ServiceCollection();
        services.AddHeadlessTenantWriteGuard();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task guard_disabled_should_preserve_current_create_without_current_tenant_behavior()
    {
        // given
        var fixture = _disabledFixture;
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
        var fixture = _enabledFixture;
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
    public async Task guard_enabled_should_reject_tenant_owned_update_without_current_tenant_before_side_effects()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "missing-tenant-update");

        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        entity.Name = "updated-without-tenant";

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<MissingTenantContextException>();
        db.EmittedLocalMessages.Should().BeEmpty();
        db.EmittedDistributedMessages.Should().BeEmpty();

        var persistedName = await _GetTenantEntityNameAsync(fixture, entityId);
        persistedName.Should().Be("missing-tenant-update");
    }

    [Fact]
    public async Task guard_enabled_should_reject_tenant_owned_physical_delete_without_current_tenant_before_side_effects()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "missing-tenant-delete");

        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        db.Tests.Remove(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<MissingTenantContextException>();
        db.EmittedLocalMessages.Should().BeEmpty();
        db.EmittedDistributedMessages.Should().BeEmpty();

        var persisted = await _TenantEntityExistsAsync(fixture, entityId);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task guard_enabled_should_stamp_added_tenant_owned_entity_under_current_tenant()
    {
        // given
        var fixture = _enabledFixture;
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
        var fixture = _enabledFixture;
        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        db.Tests.Add(new TestEntity { Name = "wrong-tenant", TenantId = "tenant-b" });

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        var exception = await act.Should().ThrowAsync<CrossTenantWriteException>();
        exception.Which.EntityType.Should().Be(typeof(TestEntity).FullName);
        exception.Which.Operation.Should().Be(nameof(EntityState.Added));
        db.EmittedLocalMessages.Should().BeEmpty();
        var persisted = await db.Tests.IgnoreMultiTenancyFilter().CountAsync(AbortToken);
        persisted.Should().Be(0);
    }

    [Fact]
    public async Task guard_enabled_should_allow_matching_tenant_create()
    {
        // given
        var fixture = _enabledFixture;
        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        // when
        var entity = new TestEntity { Name = "initial" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        // then
        var persisted = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(AbortToken);
        persisted.Name.Should().Be("initial");
        persisted.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task guard_enabled_should_allow_matching_tenant_create_with_sync_save_changes()
    {
        // given
        var fixture = _enabledFixture;
        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "sync-initial" };
        db.Tests.Add(entity);

        // when
        db.SaveChanges();

        // then
        entity.TenantId.Should().Be("tenant-a");
        var persisted = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(AbortToken);
        persisted.Name.Should().Be("sync-initial");
        persisted.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task guard_enabled_should_allow_matching_tenant_update()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "initial");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        // when
        var saved = await db.Tests.SingleAsync(x => x.Id == entityId, AbortToken);
        saved.Name = "updated";
        await db.SaveChangesAsync(AbortToken);

        // then
        var persistedName = await _GetTenantEntityNameAsync(fixture, entityId);
        persistedName.Should().Be("updated");
    }

    [Fact]
    public async Task guard_enabled_should_allow_matching_tenant_physical_delete()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "to-delete");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        // when
        var saved = await db.Tests.SingleAsync(x => x.Id == entityId, AbortToken);
        db.Tests.Remove(saved);
        await db.SaveChangesAsync(AbortToken);

        // then
        var exists = await _TenantEntityExistsAsync(fixture, entityId);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task guard_enabled_should_reject_remove_after_tenant_id_rewrite_on_tracked_entity()
    {
        // given — load entity owned by tenant-a, rewrite CurrentValue, then Remove
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "owned-by-a");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.SingleAsync(x => x.Id == entityId, AbortToken);
        // Rewrite current value to look like a different-tenant row, then attempt delete.
        db.Entry(entity).Property(nameof(TestEntity.TenantId)).CurrentValue = "tenant-b";
        db.Tests.Remove(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then — Delete must apply OriginalValue matching (parity with Modified state)
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        var persisted = await _TenantEntityExistsAsync(fixture, entityId);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task guard_enabled_should_reject_delete_when_entity_tenant_id_is_null()
    {
        // given — bypass to create a row with null TenantId, then attempt a guarded delete
        var fixture = _enabledFixture;
        var bypass = fixture.ServiceProvider.GetRequiredService<ITenantWriteGuardBypass>();
        Guid entityId;

        using (bypass.BeginBypass())
        {
            await using var seedScope = fixture.ServiceProvider.CreateAsyncScope();
            await using var seedDb = seedScope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();
            var seed = new TestEntity { Name = "null-tenant-row" };
            seedDb.Tests.Add(seed);
            await seedDb.SaveChangesAsync(AbortToken);
            entityId = seed.Id;
        }

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        db.Tests.Remove(entity);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        var exception = await act.Should().ThrowAsync<CrossTenantWriteException>();
        exception.Which.Operation.Should().Be(nameof(EntityState.Deleted));
        var persisted = await _TenantEntityExistsAsync(fixture, entityId);
        persisted.Should().BeTrue();
    }

    [Fact]
    public async Task guard_enabled_should_reject_cross_tenant_update_loaded_through_ignored_filter()
    {
        // given
        var fixture = _enabledFixture;
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
    public async Task guard_enabled_should_reject_cross_tenant_update_loaded_through_ignored_filter_with_sync_save_changes()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-b", "sync-owned-by-b");

        fixture.CurrentTenant.Id = "tenant-a";
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == entityId, AbortToken);
        entity.Name = "sync-changed-by-a";

        // when
        var act = () => db.SaveChanges();

        // then
        act.Should().Throw<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persistedName = await _GetTenantEntityNameAsync(fixture, entityId);
        persistedName.Should().Be("sync-owned-by-b");
    }

    [Fact]
    public async Task guard_enabled_should_reject_tenant_id_reassignment_on_tracked_update()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "owned-by-a");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.SingleAsync(x => x.Id == entityId, AbortToken);
        db.Entry(entity).Property(nameof(TestEntity.TenantId)).CurrentValue = "tenant-b";

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persistedTenant = await _GetTenantEntityTenantIdAsync(fixture, entityId);
        persistedTenant.Should().Be("tenant-a");
    }

    [Fact]
    public async Task guard_enabled_should_reject_clearing_tenant_id_on_tracked_update()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-a", "owned-by-a");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = await db.Tests.SingleAsync(x => x.Id == entityId, AbortToken);
        db.Entry(entity).Property(nameof(TestEntity.TenantId)).CurrentValue = null;

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persistedTenant = await _GetTenantEntityTenantIdAsync(fixture, entityId);
        persistedTenant.Should().Be("tenant-a");
    }

    [Fact]
    public async Task guard_enabled_should_reject_cross_tenant_physical_delete_loaded_through_ignored_filter()
    {
        // given
        var fixture = _enabledFixture;
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
        var fixture = _enabledFixture;
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

    [Fact(Skip = "https://github.com/xshaheen/headless-framework/issues/249")]
    public async Task guard_enabled_should_reject_attach_then_modify_cross_tenant_row()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-b", "attach-update-owned-by-b");
        var concurrencyStamp = await _GetTenantEntityConcurrencyStampAsync(fixture, entityId);

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var crafted = new TestEntity { Name = "crafted", TenantId = "tenant-a" };
        db.Entry(crafted).Property(nameof(TestEntity.Id)).CurrentValue = entityId;
        db.Entry(crafted).Property(nameof(TestEntity.ConcurrencyStamp)).CurrentValue = concurrencyStamp;
        db.Attach(crafted);
        crafted.Name = "compromised";

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var persistedName = await _GetTenantEntityNameAsync(fixture, entityId);
        persistedName.Should().Be("attach-update-owned-by-b");
    }

    [Fact(Skip = "https://github.com/xshaheen/headless-framework/issues/249")]
    public async Task guard_enabled_should_reject_attach_then_remove_cross_tenant_row()
    {
        // given
        var fixture = _enabledFixture;
        var entityId = await _SeedTenantEntityAsync(fixture, "tenant-b", "attach-delete-owned-by-b");
        var concurrencyStamp = await _GetTenantEntityConcurrencyStampAsync(fixture, entityId);

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var crafted = new TestEntity { Name = "crafted-delete", TenantId = "tenant-a" };
        db.Entry(crafted).Property(nameof(TestEntity.Id)).CurrentValue = entityId;
        db.Entry(crafted).Property(nameof(TestEntity.ConcurrencyStamp)).CurrentValue = concurrencyStamp;
        db.Attach(crafted);
        db.Tests.Remove(crafted);

        // when
        var act = async () => await db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<CrossTenantWriteException>();
        db.EmittedLocalMessages.Should().BeEmpty();

        var exists = await _TenantEntityExistsAsync(fixture, entityId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task guard_enabled_should_allow_non_tenant_entity_add_update_and_delete_without_current_tenant()
    {
        // given
        var fixture = _enabledFixture;
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
        var fixture = _enabledFixture;
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
            var crossTenant = await db.Tests.IgnoreMultiTenancyFilter().SingleAsync(x => x.Id == seededId, AbortToken);
            crossTenant.Name = "bypass-updated";
            await db.SaveChangesAsync(AbortToken);
        }

        db.Tests.Add(new TestEntity { Name = "strict-again", TenantId = "tenant-b" });

        // then
        var act = async () => await db.SaveChangesAsync(AbortToken);
        await act.Should().ThrowAsync<CrossTenantWriteException>();

        var hostWriteCount = await db
            .Tests.IgnoreMultiTenancyFilter()
            .CountAsync(x => x.Name == "host-write", AbortToken);
        hostWriteCount.Should().Be(1);
        var bypassName = await _GetTenantEntityNameAsync(fixture, seededId);
        bypassName.Should().Be("bypass-updated");
    }

    [Fact]
    public async Task multi_tenancy_filter_should_scope_execute_update_to_current_tenant()
    {
        // given
        var fixture = _enabledFixture;
        var tenantAId = await _SeedTenantEntityAsync(fixture, "tenant-a", "initial-a");
        var tenantBId = await _SeedTenantEntityAsync(fixture, "tenant-b", "initial-b");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        // when - attempt to update all rows to "bulk-updated"
        // The filter should restrict this to only tenant-a's rows.
        var updatedCount = await db.Tests.ExecuteUpdateAsync(
            s => s.SetProperty(x => x.Name, "bulk-updated"),
            AbortToken
        );

        // then
        updatedCount.Should().Be(1);

        var nameA = await _GetTenantEntityNameAsync(fixture, tenantAId);
        nameA.Should().Be("bulk-updated");

        var nameB = await _GetTenantEntityNameAsync(fixture, tenantBId);
        nameB.Should().Be("initial-b");
    }

    [Fact]
    public async Task multi_tenancy_filter_should_scope_execute_delete_to_current_tenant()
    {
        // given
        var fixture = _enabledFixture;
        var tenantAId = await _SeedTenantEntityAsync(fixture, "tenant-a", "initial-a");
        var tenantBId = await _SeedTenantEntityAsync(fixture, "tenant-b", "initial-b");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        // when - attempt to delete all rows
        // The filter should restrict this to only tenant-a's rows.
        var deletedCount = await db.Tests.ExecuteDeleteAsync(AbortToken);

        // then
        deletedCount.Should().Be(1);

        var existsA = await _TenantEntityExistsAsync(fixture, tenantAId);
        existsA.Should().BeFalse();

        var existsB = await _TenantEntityExistsAsync(fixture, tenantBId);
        existsB.Should().BeTrue();
    }

    [Fact]
    public async Task ignore_multi_tenancy_filter_should_bypass_scoping_for_bulk_operations()
    {
        // given
        var fixture = _enabledFixture;
        await _SeedTenantEntityAsync(fixture, "tenant-a", "initial-a");
        await _SeedTenantEntityAsync(fixture, "tenant-b", "initial-b");

        using var tenant = fixture.CurrentTenant.Change("tenant-a");
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        // when - ignore filter and delete all
        var deletedCount = await db.Tests.IgnoreMultiTenancyFilter().ExecuteDeleteAsync(AbortToken);

        // then
        deletedCount.Should().Be(2);
    }

    private async Task<Guid> _SeedTenantEntityAsync(
        TenantWriteGuardDbContextTestFixtureBase fixture,
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

    private async Task<string> _GetTenantEntityNameAsync(
        TenantWriteGuardDbContextTestFixtureBase fixture,
        Guid entityId
    )
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db
            .Tests.IgnoreMultiTenancyFilter()
            .Where(x => x.Id == entityId)
            .Select(x => x.Name)
            .SingleAsync(AbortToken);
    }

    private async Task<string?> _GetTenantEntityTenantIdAsync(
        TenantWriteGuardDbContextTestFixtureBase fixture,
        Guid entityId
    )
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db
            .Tests.IgnoreMultiTenancyFilter()
            .Where(x => x.Id == entityId)
            .Select(x => x.TenantId)
            .SingleAsync(AbortToken);
    }

    private async Task<string?> _GetTenantEntityConcurrencyStampAsync(
        TenantWriteGuardDbContextTestFixtureBase fixture,
        Guid entityId
    )
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db
            .Tests.IgnoreMultiTenancyFilter()
            .Where(x => x.Id == entityId)
            .Select(x => x.ConcurrencyStamp)
            .SingleAsync(AbortToken);
    }

    private async Task<bool> _GetTenantEntityIsDeletedAsync(
        TenantWriteGuardDbContextTestFixtureBase fixture,
        Guid entityId
    )
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

    private async Task<bool> _TenantEntityExistsAsync(TenantWriteGuardDbContextTestFixtureBase fixture, Guid entityId)
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        return await db.Tests.IgnoreMultiTenancyFilter().AnyAsync(x => x.Id == entityId, AbortToken);
    }

    private sealed record TenantWriteGuardOptionsMarker(Guid Id);
}
