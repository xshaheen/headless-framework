// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Testing.Tests;
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public virtual async Task caller_transaction_should_complete_each_batch_and_recover_through_fresh_context(
        bool synchronous,
        bool rollback
    )
    {
        await using (var scope = Fixture.ServiceProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
            var first = new HarnessTestEntity { Name = "first-batch" };
            first.EmitDomainEvent(new HarnessLocalMessage("first"));
            db.TestEntities.Add(first);
            if (synchronous)
            {
#pragma warning disable MA0045 // Synchronous provider conformance case.
                db.SaveChanges();
#pragma warning restore MA0045
            }
            else
            {
                await db.SaveChangesAsync(AbortToken);
            }

            first.GetDomainEvents().Should().BeEmpty();
            var firstCount = db.EmittedLocalMessages.Count;
            var second = new HarnessTestEntity { Name = "second-batch" };
            db.TestEntities.Add(second);
            if (synchronous)
            {
#pragma warning disable MA0045 // Synchronous provider conformance case.
                db.SaveChanges();
#pragma warning restore MA0045
            }
            else
            {
                await db.SaveChangesAsync(AbortToken);
            }

            second.GetDomainEvents().Should().BeEmpty();
            db.EmittedLocalMessages.Count.Should().Be(firstCount + 2);
            if (rollback)
            {
                await transaction.RollbackAsync(AbortToken);
            }
            else
            {
                await transaction.CommitAsync(AbortToken);
            }
        }

        await using var freshScope = Fixture.ServiceProvider.CreateAsyncScope();
        var fresh = freshScope.ServiceProvider.GetRequiredService<TContext>();
        (await fresh.TestEntities.CountAsync(AbortToken)).Should().Be(rollback ? 0 : 2);
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
        entity.CreatedAt.Should().Be(Fixture.Now);
        entity.CreatedById.Should().Be(Fixture.UserId);

        // then - concurrency stamp set
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();

        // then - update/delete/suspend not set
        entity.UpdatedAt.Should().BeNull();
        entity.UpdatedById.Should().BeNull();
        entity.IsDeleted.Should().BeFalse();
        entity.DeletedAt.Should().BeNull();
        entity.DeletedById.Should().BeNull();
        entity.IsSuspended.Should().BeFalse();
        entity.SuspendedAt.Should().BeNull();
        entity.SuspendedById.Should().BeNull();

        // then - local domain events emitted (Created + Changed)
        db.EmittedLocalMessages.Should().HaveCount(2);
        var createdMessage = db.EmittedLocalMessages.OfType<EntityCreatedEventData<HarnessTestEntity>>().Single();
        createdMessage.Entity.Should().Be(entity);
        var changedMessage = db.EmittedLocalMessages.OfType<EntityChangedEventData<HarnessTestEntity>>().Single();
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
        db.EmittedLocalMessages.Clear();

        // when
        entity.Name = "updated";
        await db.SaveChangesAsync(AbortToken);

        // then - update audit set
        entity.UpdatedAt.Should().Be(Fixture.Now);
        entity.UpdatedById.Should().Be(Fixture.UserId);

        // then - concurrency stamp updated
        entity.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        entity.ConcurrencyStamp.Should().NotBe(oldStamp);

        // then - local domain events emitted (Updated + Changed)
        db.EmittedLocalMessages.Should().HaveCount(2);
        var updatedMessage = db.EmittedLocalMessages.OfType<EntityUpdatedEventData<HarnessTestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = db.EmittedLocalMessages.OfType<EntityChangedEventData<HarnessTestEntity>>().Single();
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
        db.EmittedLocalMessages.Clear();

        // when
        entity.MarkDeleted();
        db.Update(entity);
        await db.SaveChangesAsync(AbortToken);

        // then - delete audit set
        entity.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().Be(Fixture.Now);
        entity.DeletedById.Should().Be(Fixture.UserId);

        // then - local domain events emitted (Updated + Changed)
        db.EmittedLocalMessages.Should().HaveCount(2);
        var updatedMessage = db.EmittedLocalMessages.OfType<EntityUpdatedEventData<HarnessTestEntity>>().Single();
        updatedMessage.Entity.Should().Be(entity);
        var changedMessage = db.EmittedLocalMessages.OfType<EntityChangedEventData<HarnessTestEntity>>().Single();
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
        entity.SuspendedAt.Should().Be(Fixture.Now);
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
        entity.EmitIntegrationEvent(new HarnessDistributedMessage("hello"));
        db.TestEntities.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then - both local and distributed events emitted
        db.EmittedLocalMessages.Should().NotBeEmpty();
        db.EmittedDistributedMessages.Should().ContainSingle();
        db.EmittedDistributedMessages.Single().Should().BeOfType<HarnessDistributedMessage>();

        await tx.CommitAsync(AbortToken);
    }

    #endregion
}
