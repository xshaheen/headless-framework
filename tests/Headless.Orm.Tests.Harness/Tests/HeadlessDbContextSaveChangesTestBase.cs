// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;
using Framework.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Entities;
using Tests.Fixtures;
using Tests.Messages;

namespace Tests.Tests;

/// <summary>
/// Base test class for HeadlessDbContext SaveChanges behavior.
/// Concrete implementations provide the fixture and context types.
/// </summary>
public abstract class HeadlessDbContextSaveChangesTestBase<TFixture, TContext> : TestBase
    where TFixture : class, IDbContextTestFixture<TContext>
    where TContext : DbContext, IHarnessDbContext
{
    protected TFixture Fixture { get; }

    protected HeadlessDbContextSaveChangesTestBase(TFixture fixture)
    {
        Fixture = fixture;
        // Ensure clean DB for each test
        using var scope = Fixture.ServiceProvider.CreateScope();
        scope.ServiceProvider.EnsureDbRecreated<TContext>();
    }

    #region Basic SaveChanges (no emitters)

    [Fact]
    public virtual async Task save_changes_without_emitters_should_not_publish_messages()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = new HarnessBasicEntity { Name = "no-op" };
        db.BasicEntities.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        var count = await db.BasicEntities.CountAsync(AbortToken);
        count.Should().Be(1);
        db.EmittedLocalMessages.Should().BeEmpty();
        db.EmittedDistributedMessages.Should().BeEmpty();
    }

    #endregion

    #region Add - Audit, ID, Concurrency, Messages

    [Fact]
    public virtual async Task save_changes_add_should_set_guid_id_create_audit_and_concurrency_stamp_and_emit_local_messages()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = new HarnessTestEntity { Name = "created", TenantId = "T1" };
        db.TestEntities.Add(entity);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then - ID generated
        entity.Id.Should().NotBe(Guid.Empty);

        // then - create audit set
        entity.DateCreated.Should().Be(Fixture.Now);
        entity.CreatedById.Should().Be(Fixture.UserId);

        // then - concurrency stamp set
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();

        // then - update/delete/suspend not set
        entity.DateUpdated.Should().BeNull();
        entity.UpdatedById.Should().BeNull();
        entity.IsDeleted.Should().BeFalse();
        entity.DateDeleted.Should().BeNull();
        entity.DeletedById.Should().BeNull();
        entity.IsSuspended.Should().BeFalse();
        entity.DateSuspended.Should().BeNull();
        entity.SuspendedById.Should().BeNull();

        // then - local messages emitted (Created + Changed)
        db.EmittedLocalMessages.Should().ContainSingle();
        var local = db.EmittedLocalMessages.Single();
        local.Emitter.Should().Be(entity);
        local.Messages.Should().HaveCount(2);
        var createdMessage = local.Messages.OfType<EntityCreatedEventData<HarnessTestEntity>>().Single();
        createdMessage.Entity.Should().Be(entity);
        var changedMessage = local.Messages.OfType<EntityChangedEventData<HarnessTestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    #endregion

    #region Update - Audit, Concurrency, Messages

    [Fact]
    public virtual async Task save_changes_update_should_set_update_audit_and_update_concurrency_stamp_and_emit_updated_message()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = new HarnessTestEntity { Name = "initial", TenantId = "T1" };
        db.TestEntities.Add(entity);
        await db.SaveChangesAsync(AbortToken);
        var oldStamp = entity.ConcurrencyStamp;

        // when
        entity.Name = "updated";
        await db.SaveChangesAsync(AbortToken);

        // then - update audit set
        entity.DateUpdated.Should().Be(Fixture.Now);
        entity.UpdatedById.Should().Be(Fixture.UserId);

        // then - concurrency stamp updated
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.ConcurrencyStamp.Should().NotBe(oldStamp);

        // then - local messages emitted (Updated + Changed)
        db.EmittedLocalMessages.Should().NotBeEmpty();
        var last = db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<HarnessTestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<HarnessTestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    #endregion

    #region Soft Delete - Audit, Messages

    [Fact]
    public virtual async Task save_changes_soft_delete_should_set_delete_audit_and_emit_deleted_message()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = new HarnessTestEntity { Name = "to-delete", TenantId = "T1" };
        db.TestEntities.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        // when
        entity.MarkDeleted();
        db.Update(entity);
        await db.SaveChangesAsync(AbortToken);

        // then - delete audit set
        entity.IsDeleted.Should().BeTrue();
        entity.DateDeleted.Should().Be(Fixture.Now);
        entity.DeletedById.Should().Be(Fixture.UserId);

        // then - local messages emitted (Updated + Changed)
        var last = db.EmittedLocalMessages[^1];
        last.Messages.Should().HaveCount(2);
        var updatedMessage = last.Messages.OfType<EntityUpdatedEventData<HarnessTestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = last.Messages.OfType<EntityChangedEventData<HarnessTestEntity>>().Single();
        changedMessage.Entity.Should().Be(entity);
    }

    #endregion

    #region Suspend - Audit

    [Fact]
    public virtual async Task save_changes_suspend_should_set_suspend_audit()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = new HarnessTestEntity { Name = "to-suspend", TenantId = "T1" };
        db.TestEntities.Add(entity);
        await db.SaveChangesAsync(AbortToken);

        // when
        entity.MarkSuspended();
        db.Update(entity);
        await db.SaveChangesAsync(AbortToken);

        // then - suspend audit set
        entity.IsSuspended.Should().BeTrue();
        entity.DateSuspended.Should().Be(Fixture.Now);
        entity.SuspendedById.Should().Be(Fixture.UserId);
    }

    #endregion

    #region Distributed Messages

    [Fact]
    public virtual async Task distributed_and_local_messages_should_publish_within_existing_transaction()
    {
        // given
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var entity = new HarnessTestEntity { Name = "with-msgs", TenantId = "T1" };
        entity.AddMessage(new HarnessDistributedMessage("hello"));
        db.TestEntities.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then - both local and distributed messages emitted
        db.EmittedLocalMessages.Should().NotBeEmpty();
        db.EmittedDistributedMessages.Should().ContainSingle();
        var dist = db.EmittedDistributedMessages.Single();
        dist.Emitter.Should().Be(entity);
        dist.Messages.Should().ContainSingle();
        dist.Messages.Single().Should().BeOfType<HarnessDistributedMessage>();

        await tx.CommitAsync(AbortToken);
    }

    #endregion
}
