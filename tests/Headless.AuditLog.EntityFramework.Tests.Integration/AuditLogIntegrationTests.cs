using System.Text.Json;
using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

public sealed class AuditLogIntegrationTests : TestBase
{
    [Fact]
    public async Task created_entity_round_trip()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Alice",
            Email = "alice@example.com",
            Amount = 99.99m,
        };
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();

        // then
        var entries = await db.Set<AuditLogEntry>().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.Action.Should().Be(AuditActionNames.Created);
        entry.ChangeType.Should().Be(AuditChangeType.Created);
        entry.EntityType.Should().Be(typeof(Order).FullName);
        entry.EntityId.Should().Be(order.Id.ToString());
        entry.OldValues.Should().BeNull();
        entry.NewValues.Should().NotBeNull();
        entry.NewValues.Should().ContainKey("CustomerName");
        entry.NewValues.Should().ContainKey("Amount");
        entry.NewValues.Should().ContainKey("IsDeleted");
        entry.UserId.Should().Be(AuditIntegrationFixture.UserId);
        entry.TenantId.Should().Be(AuditIntegrationFixture.TenantId);
        entry.CreatedAt.Should().Be(AuditIntegrationFixture.Now);

        var amount = entry.NewValues["Amount"].Should().BeOfType<JsonElement>().Subject;
        amount.GetDecimal().Should().Be(99.99m);

        var isDeleted = entry.NewValues["IsDeleted"].Should().BeOfType<JsonElement>().Subject;
        isDeleted.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task read_audit_log_query_returns_dto_results()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var readAuditLog = scope.ServiceProvider.GetRequiredService<IReadAuditLog>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Alice",
            Email = "alice@example.com",
            Amount = 12.5m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(AbortToken);

        // when
        var entries = await readAuditLog.QueryAsync(
            action: AuditActionNames.Created,
            entityType: typeof(Order).FullName,
            limit: 10,
            cancellationToken: AbortToken
        );

        // then
        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.Action.Should().Be(AuditActionNames.Created);
        entry.EntityType.Should().Be(typeof(Order).FullName);
        entry.EntityId.Should().Be(order.Id.ToString());
        entry.NewValues.Should().ContainKey("Amount");
        entry.NewValues!["Amount"].Should().BeOfType<JsonElement>();
    }

    [Fact]
    public async Task read_audit_log_query_orders_newest_entries_first_when_timestamps_match()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var readAuditLog = scope.ServiceProvider.GetRequiredService<IReadAuditLog>();

        db.Orders.Add(
            new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "First",
                Email = "first@example.com",
                Amount = 10m,
            }
        );
        await db.SaveChangesAsync(AbortToken);

        db.Orders.Add(
            new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Second",
                Email = "second@example.com",
                Amount = 20m,
            }
        );
        await db.SaveChangesAsync(AbortToken);

        // when
        var entries = await readAuditLog.QueryAsync(
            action: AuditActionNames.Created,
            entityType: typeof(Order).FullName,
            limit: 2,
            cancellationToken: AbortToken
        );

        // then
        entries.Should().HaveCount(2);
        entries[0]
            .NewValues!["CustomerName"]
            .Should()
            .BeOfType<JsonElement>()
            .Subject.GetString()
            .Should()
            .Be("Second");
        entries[1].NewValues!["CustomerName"].Should().BeOfType<JsonElement>().Subject.GetString().Should().Be("First");
    }

    [Fact]
    public async Task updated_entity_round_trip()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Bob",
            Email = "bob@example.com",
            Amount = 50m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(AbortToken);

        // Remove the created audit entry so only the update remains
        await db.Set<AuditLogEntry>().Where(e => e.Action == AuditActionNames.Created).ExecuteDeleteAsync(AbortToken);

        // when
        order.CustomerName = "Robert";
        await db.SaveChangesAsync(AbortToken);

        // then
        var entries = await db.Set<AuditLogEntry>().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.Action.Should().Be(AuditActionNames.Updated);
        entry.ChangeType.Should().Be(AuditChangeType.Updated);
        entry.ChangedFields.Should().Contain("CustomerName");
        entry.OldValues.Should().NotBeNull();
        entry.NewValues.Should().NotBeNull();
    }

    [Fact]
    public async Task deleted_entity_round_trip()
    {
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
        await db.SaveChangesAsync(AbortToken);

        await db.Set<AuditLogEntry>().Where(e => e.Action == AuditActionNames.Created).ExecuteDeleteAsync(AbortToken);

        // when
        db.Orders.Remove(order);
        await db.SaveChangesAsync(AbortToken);

        // then
        var entries = await db.Set<AuditLogEntry>().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.Action.Should().Be(AuditActionNames.Deleted);
        entry.ChangeType.Should().Be(AuditChangeType.Deleted);
        entry.OldValues.Should().NotBeNull();
        entry.NewValues.Should().BeNull();
        entry.OldValues.Should().ContainKey("CustomerName");
    }

    [Fact]
    public async Task explicit_audit_log_async_creates_entry()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        // when
        await auditLog.LogAsync(
            "pii.revealed",
            entityType: "User",
            entityId: "user-999",
            cancellationToken: AbortToken
        );
        await db.SaveChangesAsync(AbortToken);

        // then
        var entries = await db.Set<AuditLogEntry>().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.Action.Should().Be("pii.revealed");
        entry.ChangeType.Should().BeNull();
        entry.EntityType.Should().Be("User");
        entry.EntityId.Should().Be("user-999");
        entry.Success.Should().BeTrue();
    }

    [Fact]
    public async Task sensitive_property_redacted_in_stored_entry()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Dave",
            Email = "dave@secret.com",
            Amount = 10m,
        };
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        var entries = await db.Set<AuditLogEntry>().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.NewValues.Should().ContainKey("Email");
        entry.NewValues!["Email"].Should().Be("***");
    }

    [Fact]
    public async Task audit_disabled_produces_no_entries()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync(opts => opts.IsEnabled = false);
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Eve",
            Email = "eve@example.com",
            Amount = 5m,
        };
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(AbortToken);

        // then
        var count = await db.Set<AuditLogEntry>().CountAsync(AbortToken);
        count.Should().Be(0);
    }
}
