using Framework.Domains;
using Framework.Orm.EntityFramework.Contexts;
using Framework.Testing.Order;
using Framework.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

[TestCaseOrderer(typeof(AlfaTestsOrderer))]
[Collection<HeadlessDbContextTestFixture>]
public sealed class HeadlessDbContextTests : TestBase
{
    private readonly HeadlessDbContextTestFixture _fixture;

    public HeadlessDbContextTests(HeadlessDbContextTestFixture fixture)
    {
        _fixture = fixture;
        using var scope = _fixture.ServiceProvider.CreateScope();
        scope.ServiceProvider.EnsureDbRecreated<TestHeadlessDbContext>();
    }

    [Fact]
    public async Task save_changes_without_emitters_should_not_publish_messages()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
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
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "created", TenantId = "T1" };

        db.Tests.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.Id.Should().NotBe(Guid.Empty);
        entity.DateCreated.Should().Be(HeadlessDbContextTestFixture.Now);
        entity.CreatedById.Should().Be(HeadlessDbContextTestFixture.UserId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.DateUpdated.Should().BeNull();
        entity.UpdatedById.Should().BeNull();
        entity.IsDeleted.Should().BeFalse();
        entity.DateDeleted.Should().BeNull();
        entity.DeletedById.Should().BeNull();
        entity.IsSuspended.Should().BeFalse();
        entity.DateSuspended.Should().BeNull();
        entity.SuspendedById.Should().BeNull();

        // Local messages: Created + Changed
        db.EmittedLocalMessages.Should().ContainSingle();
        var local = db.EmittedLocalMessages.Single();
        local.Emitter.Should().Be(entity);
        local.Messages.Should().HaveCount(2);
        var createdMessage = local.Messages.OfType<EntityCreatedEventData<TestEntity>>().Single();
        createdMessage.Entity.Should().Be(entity);
        var changedMessage = local.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Update

    [Fact]
    public async Task save_changes_update_should_set_update_audit_and_update_concurrency_stamp_and_emit_updated_message()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "initial", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        var oldStamp = entity.ConcurrencyStamp;

        // when
        entity.Name = "updated";
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.DateUpdated.Should().Be(HeadlessDbContextTestFixture.Now);
        entity.UpdatedById.Should().Be(HeadlessDbContextTestFixture.UserId);
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.ConcurrencyStamp.Should().NotBe(oldStamp);

        // Local messages: Updated + Changed
        db.EmittedLocalMessages.Should().NotBeEmpty();
        var last = db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Delete

    [Fact]
    public async Task save_changes_soft_delete_should_set_delete_audit_and_emit_deleted_message()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "to-delete", TenantId = "T1" };
        db.Tests.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        // when
        entity.MarkDeleted();
        db.Update(entity);
        await db.SaveChangesAsync(AbortToken);

        // then
        entity.IsDeleted.Should().BeTrue();
        entity.DateDeleted.Should().Be(HeadlessDbContextTestFixture.Now);
        entity.DeletedById.Should().Be(HeadlessDbContextTestFixture.UserId);

        var last = db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<TestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<TestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    // Suspend

    [Fact]
    public async Task save_changes_suspend_should_set_suspend_audit()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
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
        entity.DateSuspended.Should().Be(HeadlessDbContextTestFixture.Now);
        entity.SuspendedById.Should().Be(HeadlessDbContextTestFixture.UserId);
    }

    // Publish messages

    [Fact]
    public async Task distributed_and_local_messages_should_publish_within_existing_transaction()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var entity = new TestEntity { Name = "with-msgs", TenantId = "T1" };
        entity.AddMessage(new TestDistributedMessage("hello"));
        db.Tests.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        db.EmittedLocalMessages.Should().NotBeEmpty();
        db.EmittedDistributedMessages.Should().ContainSingle();
        var dist = db.EmittedDistributedMessages.Single();
        dist.Emitter.Should().Be(entity);
        dist.Messages.Should().ContainSingle();
        dist.Messages.Single().Should().BeOfType<TestDistributedMessage>();

        await tx.CommitAsync(AbortToken);
    }

    // ExecuteTransactionAsync

    [Fact]
    public async Task execute_transaction_async_should_commit_or_rollback_by_return_value()
    {
        // commit path
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        var committed = false;

        await db.ExecuteTransactionAsync(async () =>
        {
            await db.Basics.AddAsync(new BasicEntity { Name = "in-tx" });
            committed = true;

            return true;
        });

        (await db.Basics.CountAsync(AbortToken)).Should().Be(1);
        committed.Should().BeTrue();

        // rollback path
        await db.ExecuteTransactionAsync(async () =>
        {
            await db.Basics.AddAsync(new BasicEntity { Name = "rolled" });

            return false;
        });

        (await db.Basics.CountAsync(AbortToken)).Should().Be(1);
    }

    // Global filters

    [Fact]
    public async Task global_filters_should_filter_by_tenant_delete_and_suspend_flags_and_can_be_disabled()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
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
        using (_fixture.CurrentTenant.Change(null))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("e");
        }

        using (_fixture.CurrentTenant.Change("TENANT-1"))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("a");
        }

        using (_fixture.CurrentTenant.Change(null))
        {
            var items = await db.Tests.Select(x => x.Name).ToArrayAsync(AbortToken);
            items.Should().BeEquivalentTo("e");
        }

        using (_fixture.CurrentTenant.Change("TENANT-2"))
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
        using (_fixture.CurrentTenant.Change("TENANT-1"))
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
