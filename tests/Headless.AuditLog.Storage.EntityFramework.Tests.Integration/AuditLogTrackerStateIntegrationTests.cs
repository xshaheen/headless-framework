// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

public sealed class AuditLogTrackerStateIntegrationTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_keep_current_values_of_modified_entity_after_audited_save(bool synchronous)
    {
        // Regression: the audit-only second save suppressed Modified entries by moving them to
        // Unchanged, which makes EF reject the pending changes (current values roll back to the
        // originals). The restore put back OriginalValue and IsModified but not the current values,
        // so the tracked entity kept its pre-save values while the database kept the saved ones.

        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new VersionedOrder { Id = Guid.NewGuid(), CustomerName = "Bob" };
        db.VersionedOrders.Add(order);
        await _SaveAsync(db, synchronous);

        // when
        order.CustomerName = "Robert";
        await _SaveAsync(db, synchronous);

        // then — the tracked entity and entry keep the saved values, not the pre-save originals.
        order.CustomerName.Should().Be("Robert");
        var entry = db.Entry(order);
        entry.State.Should().Be(EntityState.Unchanged);
        entry.Property(e => e.CustomerName).CurrentValue.Should().Be("Robert");
        entry.Property(e => e.CustomerName).OriginalValue.Should().Be("Robert");

        // and — the store-generated computed column read back by the business save survives too.
        order.NameUpper.Should().Be("ROBERT");
        entry.Property(e => e.NameUpper).CurrentValue.Should().Be("ROBERT");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_allow_second_save_of_same_modified_entity_after_audited_save(bool synchronous)
    {
        // Regression companion: the lost current values also baked the pre-save concurrency stamp
        // into the accepted original, so a second save of the same entity in one scope matched zero
        // rows and failed with DbUpdateConcurrencyException.

        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new VersionedOrder { Id = Guid.NewGuid(), CustomerName = "Bob" };
        db.VersionedOrders.Add(order);
        await _SaveAsync(db, synchronous);
        order.CustomerName = "Robert";
        await _SaveAsync(db, synchronous);

        // when — modify the same tracked entity again and save in the same scope.
        order.CustomerName = "Bobby";
        await _SaveAsync(db, synchronous);

        // then — the save commits with no concurrency failure and the row holds the final value.
        var persisted = await db.VersionedOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id, AbortToken);
        persisted.CustomerName.Should().Be("Bobby");
        persisted.NameUpper.Should().Be("BOBBY");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_keep_store_generated_key_of_added_entity_after_audited_save(bool synchronous)
    {
        // Regression guard for the Added entries the snapshot restore also touches.

        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new GeneratedOrder { CustomerName = "Generated" };
        db.GeneratedOrders.Add(order);

        // when
        await _SaveAsync(db, synchronous);

        // then — the store-generated key read back by the business save survives the restore.
        order.Id.Should().BePositive();
        var entry = db.Entry(order);
        entry.State.Should().Be(EntityState.Unchanged);
        entry.Property(e => e.Id).CurrentValue.Should().Be(order.Id);

        var persisted = await db.GeneratedOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id, AbortToken);
        persisted.CustomerName.Should().Be("Generated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_leave_deleted_entry_detached_after_audited_save(bool synchronous)
    {
        // Regression guard for the Deleted entries the snapshot restore also touches.

        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Carol",
            Email = "carol@example.com",
            Amount = 20m,
        };
        db.Orders.Add(order);
        await _SaveAsync(db, synchronous);

        // when
        db.Orders.Remove(order);
        await _SaveAsync(db, synchronous);

        // then
        db.Entry(order).State.Should().Be(EntityState.Detached);
        (await db.Orders.CountAsync(AbortToken)).Should().Be(0);
    }

    private static Task<int> _SaveAsync(AuditTestDbContext db, bool synchronous)
    {
        return synchronous ? Task.FromResult(db.SaveChanges()) : db.SaveChangesAsync(AbortToken);
    }
}
