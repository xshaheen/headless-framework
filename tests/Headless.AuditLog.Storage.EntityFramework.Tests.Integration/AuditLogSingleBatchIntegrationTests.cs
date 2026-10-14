// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

/// <summary>
/// Audit rows whose entity id is known when the change is captured commit in the same <c>SaveChanges</c> as the
/// change; rows for store-generated keys wait for the generated value and follow in a second save.
/// </summary>
public sealed class AuditLogSingleBatchIntegrationTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_save_audit_rows_of_client_keyed_inserts_in_the_same_save(bool synchronous)
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var saves = _Count(db);
        var orders = Enumerable
            .Range(0, 3)
            .Select(i => new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = $"c{i}",
                Amount = i,
            })
            .ToArray();
        db.Orders.AddRange(orders);
        saves.Reset();

        // when
        var written = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then — one database save, a return value that counts only the caller's rows, and correct audit ids
        saves.Count.Should().Be(1);
        written.Should().Be(3);
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty("committed audit rows leave the tracker");
        var rows = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);
        rows.Select(r => r.EntityId).Should().BeEquivalentTo(orders.Select(o => o.Id.ToString()));
        rows.Should().OnlyContain(r => r.ChangeType == AuditChangeType.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_defer_audit_rows_of_store_generated_keys_until_the_key_exists(bool synchronous)
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var saves = _Count(db);
        var order = new GeneratedOrder { CustomerName = "generated" };
        db.GeneratedOrders.Add(order);
        saves.Reset();

        // when
        var written = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then
        saves.Count.Should().Be(2);
        written.Should().Be(1);
        order.Id.Should().BePositive();
        var row = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(AbortToken);
        row.EntityId.Should().Be(order.Id.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_split_a_mixed_batch_by_whether_the_key_is_known(bool synchronous)
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var saves = _Count(db);
        var clientKeyed = new Order { Id = Guid.NewGuid(), CustomerName = "client" };
        var generated = new GeneratedOrder { CustomerName = "generated" };
        db.Orders.Add(clientKeyed);
        db.GeneratedOrders.Add(generated);
        saves.Reset();

        // when
        var written = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then
        saves.Count.Should().Be(2);
        written.Should().Be(2);
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
        var rows = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);
        rows.Should().HaveCount(2);
        rows.Single(r => r.EntityType == typeof(Order).FullName).EntityId.Should().Be(clientKeyed.Id.ToString());
        rows.Single(r => r.EntityType == typeof(GeneratedOrder).FullName).EntityId.Should().Be(generated.Id.ToString());
    }

    [Fact]
    public async Task should_leave_no_audit_row_behind_when_the_entity_save_fails()
    {
        // given — the second insert reuses a primary key, so the entity save fails after the audit row was added to it
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var id = Guid.NewGuid();
        db.Orders.Add(new Order { Id = id, CustomerName = "first" });
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();
        db.Orders.Add(new Order { Id = id, CustomerName = "duplicate" });

        // when
        var act = () => db.SaveChangesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty("a failed save discards the audit rows it added");
        (await db.Set<AuditLogEntry>().AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_detach_audit_rows_without_accepting_their_changes()
    {
        // given — accepting an audit row would pay a value-comparer snapshot for its JSON columns just before the
        // row leaves the tracker, so it must go Added -> Detached and never pass through Unchanged
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var auditTransitions = new List<EntityState>();
        db.ChangeTracker.StateChanged += (_, e) =>
        {
            if (e.Entry.Entity is AuditLogEntry)
            {
                auditTransitions.Add(e.NewState);
            }
        };
        db.Orders.Add(new Order { Id = Guid.NewGuid(), CustomerName = "detach" });

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        auditTransitions.Should().NotContain(EntityState.Unchanged);
        auditTransitions.Should().EndWith(EntityState.Detached);
    }

    // DbContext.SavingChanges fires once per database save, so a pipeline that saves twice shows as two.
    private static SaveCounter _Count(DbContext db)
    {
        var counter = new SaveCounter();
        db.SavingChanges += (_, _) => counter.Count++;

        return counter;
    }

    private sealed class SaveCounter
    {
        public int Count { get; set; }

        public void Reset() => Count = 0;
    }
}
