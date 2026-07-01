using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Order;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

[TestCaseOrderer(typeof(AlphaTestsOrderer))]
[Collection<HeadlessDbContextTestFixture>]
public sealed class HeadlessDbContextTests(HeadlessDbContextTestFixture fixture) : TestBase
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();
        await db.Tests.IgnoreQueryFilters().ExecuteDeleteAsync(AbortToken);
        await db.Basics.ExecuteDeleteAsync(AbortToken);
        await db.LongKeyed.ExecuteDeleteAsync(AbortToken);
    }

    [Fact]
    public async Task save_changes_without_emitters_should_not_publish_messages()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new BasicEntity { Name = "no-op" };
        db.Basics.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        var basicCount = await db.Basics.CountAsync(AbortToken);
        basicCount.Should().Be(1);
        db.EmittedLocalMessages.Should().BeEmpty();
        db.EmittedDistributedMessages.Should().BeEmpty();
    }

    // Add

    [Fact]
    public async Task save_changes_add_should_set_guid_id_create_audit_and_concurrency_stamp_and_emit_local_messages()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "created", TenantId = "T1" };

        db.Tests.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.Id.Should().NotBe(Guid.Empty);
        entity.DateCreated.Should().Be(fixture.Now);
        entity.CreatedById.Should().Be(fixture.UserId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.DateUpdated.Should().BeNull();
        entity.UpdatedById.Should().BeNull();
        entity.IsDeleted.Should().BeFalse();
        entity.DateDeleted.Should().BeNull();
        entity.DeletedById.Should().BeNull();
        entity.IsSuspended.Should().BeFalse();
        entity.DateSuspended.Should().BeNull();
        entity.SuspendedById.Should().BeNull();

        // Local domain events: Created + Changed
        db.EmittedLocalMessages.Should().HaveCount(2);
        var createdMessage = db.EmittedLocalMessages.OfType<EntityCreatedEventData<TestEntity>>().Single();
        createdMessage.Entity.Should().Be(entity);
        var changedMessage = db.EmittedLocalMessages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    [Fact]
    public async Task add_should_stamp_guid_id_at_track_time_so_many_empty_keyed_entities_can_be_added_before_save()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var a = new TestEntity { Name = "a", TenantId = "T1" };
        var b = new TestEntity { Name = "b", TenantId = "T1" };
        var c = new TestEntity { Name = "c", TenantId = "T1" };

        // when - EF Core's value generator produces the Guid as each entity transitions to Added, not at
        // SaveChanges. Adding several empty-keyed entities therefore does not collide in the identity map.
        db.Tests.Add(a);
        db.Tests.Add(b);
        db.Tests.Add(c);

        // then - each entity already carries a distinct, non-empty key before SaveChanges is ever called.
        a.Id.Should().NotBe(Guid.Empty);
        b.Id.Should().NotBe(Guid.Empty);
        c.Id.Should().NotBe(Guid.Empty);
        new[] { a.Id, b.Id, c.Id }.Distinct().Should().HaveCount(3);

        await db.SaveChangesAsync(AbortToken);

        var persisted = await db.Tests.IgnoreQueryFilters().CountAsync(AbortToken);
        persisted.Should().Be(3);
    }

    [Fact]
    public async Task add_should_not_stamp_long_id_at_track_time()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var a = new LongKeyedEntity { Name = "a" };
        var b = new LongKeyedEntity { Name = "b" };

        // when - numeric keys are not owned by Headless; EF/database/provider configuration decides generation.
        db.LongKeyed.Add(a);
        db.LongKeyed.Add(b);

        // then - no framework value generator stamps long IDs at tracking time.
        a.Id.Should().Be(0);
        b.Id.Should().Be(0);

        await db.SaveChangesAsync(AbortToken);

        a.Id.Should().BePositive();
        b.Id.Should().BePositive();
        a.Id.Should().NotBe(b.Id);

        var persisted = await db.LongKeyed.CountAsync(AbortToken);
        persisted.Should().Be(2);
    }

    // Update

    [Fact]
    public async Task save_changes_update_should_set_update_audit_and_update_concurrency_stamp_and_emit_updated_message()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "initial", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        var oldStamp = entity.ConcurrencyStamp;
        db.EmittedLocalMessages.Clear();

        // when
        entity.Name = "updated";
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.DateUpdated.Should().Be(fixture.Now);
        entity.UpdatedById.Should().Be(fixture.UserId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.ConcurrencyStamp.Should().NotBe(oldStamp);

        // Local domain events: Updated + Changed
        db.EmittedLocalMessages.Should().HaveCount(2);
        var updatedMessage = db.EmittedLocalMessages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = db.EmittedLocalMessages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Delete

    [Fact]
    public async Task save_changes_soft_delete_should_set_delete_audit_and_emit_deleted_message()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "to-delete", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        db.EmittedLocalMessages.Clear();

        // when
        entity.MarkDeleted();
        db.Update(entity);
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.IsDeleted.Should().BeTrue();
        entity.DateDeleted.Should().Be(fixture.Now);
        entity.DeletedById.Should().Be(fixture.UserId);

        db.EmittedLocalMessages.Should().HaveCount(2);
        var updatedMessage = db.EmittedLocalMessages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = db.EmittedLocalMessages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Suspend

    [Fact]
    public async Task save_changes_suspend_should_set_suspend_audit()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "to-suspend", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        // when
        entity.MarkSuspended();
        db.Update(entity);
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.IsSuspended.Should().BeTrue();
        entity.DateSuspended.Should().Be(fixture.Now);
        entity.SuspendedById.Should().Be(fixture.UserId);
    }

    // Publish messages

    [Fact]
    public async Task distributed_and_local_messages_should_publish_within_existing_transaction()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "with-msgs", TenantId = "T1" };
        entity.AddIntegrationEvent(new TestDistributedMessage("hello"));
        db.Tests.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        db.EmittedLocalMessages.Should().NotBeEmpty();
        db.EmittedDistributedMessages.Should().ContainSingle();
        db.EmittedDistributedMessages.Single().Should().BeOfType<TestDistributedMessage>();

        await tx.CommitAsync(AbortToken);
    }

    // ExecuteTransactionAsync

    [Fact]
    public async Task execute_transaction_async_should_commit_when_operation_succeeds()
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        await db.ExecuteTransactionAsync(
            async (ctx, ct) =>
            {
                await ctx.Set<BasicEntity>().AddAsync(new BasicEntity { Name = "in-tx" }, ct);
                await ctx.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        (await db.Basics.CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task execute_transaction_async_should_rollback_when_operation_throws()
    {
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var act = async () =>
            await db.ExecuteTransactionAsync(
                async (ctx, ct) =>
                {
                    await ctx.Set<BasicEntity>().AddAsync(new BasicEntity { Name = "rolled" }, ct);
                    await ctx.SaveChangesAsync(ct);
                    throw new InvalidOperationException("simulated failure");
                },
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Basics.CountAsync(AbortToken)).Should().Be(0);
    }

    // Global filters

    [Fact]
    public async Task global_filters_should_filter_by_tenant_delete_and_suspend_flags_and_can_be_disabled()
    {
        // given
        await using var scope = fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var a = new TestEntity { Name = "a", TenantId = "TENANT-1" };
        var b = new TestEntity { Name = "b", TenantId = "TENANT-2" };
        var c = new TestEntity { Name = "c", TenantId = "TENANT-1" };
        var d = new TestEntity { Name = "d", TenantId = "TENANT-1" };
        var e = new TestEntity { Name = "e", TenantId = null };
        await db.Tests.AddRangeAsync(a, b, c, d, e);
        await db.SaveChangesAsync(AbortToken);

        // soft delete c
        c.MarkDeleted();
        // suspend d
        d.MarkSuspended();
        db.UpdateRange(c, d);
        await db.SaveChangesAsync(AbortToken);

        // when/then: default filters on
        using (fixture.CurrentTenant.Change(null))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("e");
        }

        using (fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("a");
        }

        using (fixture.CurrentTenant.Change(null))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("e");
        }

        using (fixture.CurrentTenant.Change("TENANT-2"))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("b");
        }

        // Allow deleted, Current tenant is <null>
        {
            var items = await db.Tests.IgnoreNotDeletedFilter().Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("e");
        }

        // Allow deleted, Current tenant is TENANT-1
        using (fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.Tests.IgnoreNotDeletedFilter().Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("a", "c");
        }

        {
            var items = await db.Tests.IgnoreMultiTenancyFilter().Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("a", "b", "e");
        }

        // disable all -> all visible
        {
            var items = await db
                .Tests.IgnoreMultiTenancyFilter()
                .IgnoreNotDeletedFilter()
                .IgnoreNotSuspendedFilter()
                .Select(x => x.Name)
                .ToArrayAsync(AbortToken);

            items.Should().BeEquivalentTo("a", "b", "c", "d", "e");
        }
    }
}
