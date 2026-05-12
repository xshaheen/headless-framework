using System.Globalization;
using System.Text.Json;
using Headless.AuditLog;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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
        entry.CreatedAt.Should().Be(AuditIntegrationFixture.Now.UtcDateTime);
        entry.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);

        var amount = entry.NewValues["Amount"].Should().BeOfType<JsonElement>().Subject;
        amount.GetDecimal().Should().Be(99.99m);

        var isDeleted = entry.NewValues["IsDeleted"].Should().BeOfType<JsonElement>().Subject;
        isDeleted.GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task save_changes_with_automatic_audit_marks_entity_unchanged_after_save()
    {
        // Regression guard for the AcceptAllChanges branch in CompleteSuccessfulSave when audit
        // entries are present. Existing `created_entity_round_trip` clears the change tracker
        // immediately after saving, so it does not observe post-save EntityState.

        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Hank",
            Email = "hank@example.com",
            Amount = 1m,
        };
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(AbortToken); // default: acceptAllChangesOnSuccess = true

        // then
        db.Entry(order).State.Should().Be(EntityState.Unchanged);
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
    }

    [Fact]
    public async Task save_changes_false_with_automatic_audit_preserves_entity_state_and_persists_audit_once()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new GeneratedOrder { CustomerName = "Generated" };
        db.GeneratedOrders.Add(order);

        // when
        await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, AbortToken);

        // then
        order.Id.Should().BeGreaterThan(0);
        db.Entry(order).State.Should().Be(EntityState.Added);
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();

        var entries = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.Action.Should().Be(AuditActionNames.Created);
        entry.EntityType.Should().Be(typeof(GeneratedOrder).FullName);
        entry.EntityId.Should().Be(order.Id.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task sync_save_changes_false_with_automatic_audit_preserves_entity_state_and_persists_audit_once()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new GeneratedOrder { CustomerName = "Generated" };
        db.GeneratedOrders.Add(order);

        // when
        db.SaveChanges(acceptAllChangesOnSuccess: false);

        // then
        order.Id.Should().BePositive();
        db.Entry(order).State.Should().Be(EntityState.Added);
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();

        var entries = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);
        entries.Should().ContainSingle();

        var entry = entries[0];
        entry.Action.Should().Be(AuditActionNames.Created);
        entry.EntityType.Should().Be(typeof(GeneratedOrder).FullName);
        entry.EntityId.Should().Be(order.Id.ToString(CultureInfo.InvariantCulture));
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
        var readAuditLog = scope.ServiceProvider.GetRequiredService<IReadAuditLog<AuditTestDbContext>>();

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
        var readAuditLog = scope.ServiceProvider.GetRequiredService<IReadAuditLog<AuditTestDbContext>>();

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
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog<AuditTestDbContext>>();

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
    public async Task save_changes_preserves_explicit_audit_entries_when_automatic_audit_is_present()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLog<AuditTestDbContext>>();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Heidi",
            Email = "heidi@example.com",
            Amount = 42m,
        };

        await auditLog.LogAsync(
            "pii.revealed",
            entityType: "User",
            entityId: "user-999",
            cancellationToken: AbortToken
        );
        db.Orders.Add(order);

        // when
        await db.SaveChangesAsync(AbortToken);
        db.ChangeTracker.Clear();

        // then
        var entries = await db.Set<AuditLogEntry>().AsNoTracking().ToListAsync(AbortToken);

        entries.Should().HaveCount(2);

        entries
            .Should()
            .ContainSingle(e => e.Action == "pii.revealed" && e.EntityType == "User" && e.EntityId == "user-999");

        entries
            .Should()
            .ContainSingle(e => e.Action == AuditActionNames.Created && e.EntityType == typeof(Order).FullName);
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
        var email = entry.NewValues!["Email"].Should().BeOfType<JsonElement>().Subject;
        email.GetString().Should().Be("***");
    }

    [Fact]
    public async Task save_changes_detaches_audit_entries_when_publish_throws()
    {
        // Regression guard for the catch-time Detach() path in the save runtime: once audit
        // entries have been persisted, any later failure (e.g. publish) must detach the now-tracked
        // audit entities so the change tracker no longer reports them as Added.

        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync<ThrowingPublishAuditTestDbContext>(
            configure: null,
            configureServices: services => services.AddHeadlessMessageDispatcher<ThrowingHeadlessMessageDispatcher>()
        );
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<ThrowingPublishAuditTestDbContext>();

        var order = new EmittingOrder { Id = Guid.NewGuid(), Name = "emits" };
        order.Emit(new TestDistributedMessage(Guid.NewGuid().ToString("N")));
        db.EmittingOrders.Add(order);

        // when / then
        var act = async () => await db.SaveChangesAsync(AbortToken);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            ThrowingPublishAuditTestDbContext.PublishFailureMessage
        );

        db.ChangeTracker.Entries<AuditLogEntry>().Where(e => e.State == EntityState.Added).Should().BeEmpty();
    }

    [Fact]
    public async Task save_changes_detaches_audit_entries_when_audit_commit_throws()
    {
        // Regression guard for failure inside HeadlessAuditPersistence before it returns handles
        // to the outer save pipeline catch block.

        // given
        var interceptor = new ThrowOnceOnAuditSaveInterceptor();
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync(
            configure: null,
            configureDbContext: builder => builder.AddInterceptors(interceptor)
        );
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        db.Orders.Add(
            new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Grace",
                Email = "grace@example.com",
                Amount = 11m,
            }
        );

        // when / then
        var act = async () => await db.SaveChangesAsync(AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated audit save failure.");

        db.ChangeTracker.Entries<AuditLogEntry>().Where(e => e.State == EntityState.Added).Should().BeEmpty();
    }

    [Fact]
    public async Task save_changes_succeeds_when_audit_capture_throws()
    {
        // Regression guard for CaptureEntries' swallow-and-warn contract: a buggy IAuditChangeCapture
        // must not abort the entity save; the audit table simply receives no entry for that batch.

        // given
        var throwingCapture = Substitute.For<IAuditChangeCapture>();
        throwingCapture
            .CaptureChanges(
                Arg.Any<IEnumerable<object>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset>()
            )
            .Returns(_ => throw new InvalidOperationException("Simulated capture failure."));

        var (sp, conn) = await AuditIntegrationFixture.CreateAsync(
            configure: null,
            configureServices: services =>
            {
                services.Unregister<IAuditChangeCapture>();
                services.AddSingleton(throwingCapture);
            }
        );
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = "Frank",
            Email = "frank@example.com",
            Amount = 7m,
        };
        db.Orders.Add(order);

        // when
        var saved = await db.SaveChangesAsync(AbortToken);

        // then
        saved.Should().BeGreaterThan(0);
        var auditCount = await db.Set<AuditLogEntry>().AsNoTracking().CountAsync(AbortToken);
        auditCount.Should().Be(0);
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

    private sealed class ThrowOnceOnAuditSaveInterceptor : SaveChangesInterceptor
    {
        private bool _hasThrown;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        )
        {
            ThrowIfAuditSave(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            ThrowIfAuditSave(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowIfAuditSave(DbContext? context)
        {
            if (_hasThrown || context is null)
            {
                return;
            }

            var hasPendingAuditRow = context
                .ChangeTracker.Entries<AuditLogEntry>()
                .Any(entry => entry.State == EntityState.Added);

            if (!hasPendingAuditRow)
            {
                return;
            }

            _hasThrown = true;
            throw new InvalidOperationException("Simulated audit save failure.");
        }
    }
}
