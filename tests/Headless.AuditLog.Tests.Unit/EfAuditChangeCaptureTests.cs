// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.Core;

namespace Tests;

// ---------------------------------------------------------------------------
// Test entities
// ---------------------------------------------------------------------------

public class Order : IAuditTracked
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";

    [AuditSensitive]
    public string Email { get; set; } = "";

    [AuditSensitive(SensitiveDataStrategy.Exclude)]
    public string Phone { get; set; } = "";

    [AuditIgnore]
    public DateTime LastComputedAt { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsSuspended { get; set; }
    public decimal Amount { get; set; }
}

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

[AuditIgnore]
public class InternalLog
{
    public Guid Id { get; set; }
    public string Message { get; set; } = "";
}

public class Customer : IAuditTracked
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Address Address { get; set; } = new();
}

public class PropertyTransformOrder : IAuditTracked
{
    public Guid Id { get; set; }

    [AuditSensitive(SensitiveDataStrategy.Transform)]
    public string Secret { get; set; } = "";
}

public class FrameworkManagedOrder : IAuditTracked
{
    public Guid Id { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public string Name { get; set; } = "";
}

public class CompositeKeyOrder : IAuditTracked
{
    public string TenantId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string Name { get; set; } = "";
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

// ---------------------------------------------------------------------------
// Test DbContext
// ---------------------------------------------------------------------------

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InternalLog> InternalLogs => Set<InternalLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PropertyTransformOrder> PropertyTransformOrders => Set<PropertyTransformOrder>();
    public DbSet<FrameworkManagedOrder> FrameworkManagedOrders => Set<FrameworkManagedOrder>();
    public DbSet<CompositeKeyOrder> CompositeKeyOrders => Set<CompositeKeyOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().OwnsOne(c => c.Address);
        modelBuilder.Entity<Order>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<Product>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<InternalLog>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<Customer>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<PropertyTransformOrder>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<FrameworkManagedOrder>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<CompositeKeyOrder>().HasKey(e => new { e.TenantId, e.OrderId });
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

public sealed class EfAuditChangeCaptureTests : TestBase
{
    private static readonly DateTimeOffset _Timestamp = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private const string _UserId = "user-1";
    private const string _AccountId = "account-1";
    private const string _TenantId = "tenant-1";
    private const string _CorrelationId = "correlation-1";

    // Each test gets its own in-memory SQLite connection so databases are isolated
    private static (TestDbContext db, SqliteConnection conn) _CreateDb(
        Action<DbContextOptionsBuilder<TestDbContext>>? configure = null
    )
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        var builder = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(conn);

        configure?.Invoke(builder);

        var db = new TestDbContext(builder.Options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static EfAuditChangeCapture _CreateSut(
        Action<AuditLogOptions>? configure = null,
        ILogger<EfAuditChangeCapture>? logger = null
    )
    {
        var opts = new AuditLogOptions();
        configure?.Invoke(opts);
        return new EfAuditChangeCapture(
            Options.Create(opts),
            logger ?? Substitute.For<ILogger<EfAuditChangeCapture>>()
        );
    }

    private static IReadOnlyList<AuditLogEntryData> _Capture(EfAuditChangeCapture sut, TestDbContext db)
    {
        var entries = db.ChangeTracker.Entries().Select(e => (object)e);
        return sut.CaptureChanges(entries, _UserId, _AccountId, _TenantId, _CorrelationId, _Timestamp);
    }

    // ---------------------------------------------------------------------------

    [Fact]
    public async Task created_entity_captures_new_values_only()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "alice@example.com",
                Phone = "555-1234",
            };
            db.Orders.Add(order);

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.Action.Should().Be(AuditActionNames.Created);
            entry.ChangeType.Should().Be(AuditChangeType.Created);
            entry.NewValues.Should().NotBeNull();
            entry.OldValues.Should().BeNull();
            entry.NewValues!.Should().ContainKey("CustomerName");
        }
    }

    [Fact]
    public async Task updated_entity_captures_old_and_new_values_for_changed_fields()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var orderId = Guid.NewGuid();
            var order = new Order
            {
                Id = orderId,
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // when
            order.CustomerName = "Bob";
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.Action.Should().Be(AuditActionNames.Updated);
            entry.ChangeType.Should().Be(AuditChangeType.Updated);
            entry.ChangedFields.Should().Contain("CustomerName");
            entry.OldValues.Should().ContainKey("CustomerName").WhoseValue.Should().Be("Alice");
            entry.NewValues.Should().ContainKey("CustomerName").WhoseValue.Should().Be("Bob");
        }
    }

    [Fact]
    public async Task deleted_entity_captures_old_values_only()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // when
            db.Orders.Remove(order);

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.Action.Should().Be(AuditActionNames.Deleted);
            entry.ChangeType.Should().Be(AuditChangeType.Deleted);
            entry.OldValues.Should().NotBeNull();
            entry.NewValues.Should().BeNull();
            entry.OldValues!.Should().ContainKey("CustomerName");
        }
    }

    [Fact]
    public async Task audit_ignore_property_excluded_from_all_dictionaries()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
                LastComputedAt = DateTime.UtcNow,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            order.CustomerName = "Bob";
            order.LastComputedAt = DateTime.UtcNow.AddMinutes(1);
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.ChangedFields.Should().NotContain("LastComputedAt");
            entry.OldValues.Should().NotContainKey("LastComputedAt");
            entry.NewValues.Should().NotContainKey("LastComputedAt");
        }
    }

    [Fact]
    public async Task audit_sensitive_redact_replaces_value_with_stars()
    {
        // given - Email has [AuditSensitive] with default Redact strategy
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "alice@secret.com",
                Phone = "555",
            };
            db.Orders.Add(order);

            var sut = _CreateSut(); // default strategy is Redact

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.NewValues.Should().ContainKey("Email").WhoseValue.Should().Be("***");
        }
    }

    [Fact]
    public async Task audit_sensitive_exclude_omits_property_entirely()
    {
        // given - Phone has [AuditSensitive(Strategy = Exclude)]
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555-secret",
            };
            db.Orders.Add(order);

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.NewValues.Should().NotContainKey("Phone");
        }
    }

    [Fact]
    public async Task audit_sensitive_transform_calls_transformer()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "alice@secret.com",
                Phone = "555",
            };
            db.Orders.Add(order);

            var sut = _CreateSut(opts =>
            {
                opts.SensitiveDataStrategy = SensitiveDataStrategy.Transform;
                opts.SensitiveValueTransformer = ctx => $"[MASKED:{ctx.PropertyName}]";
            });

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            // Email uses [AuditSensitive] without explicit strategy, so falls back to global Transform
            entry.NewValues.Should().ContainKey("Email").WhoseValue.Should().Be("[MASKED:Email]");
        }
    }

    [Fact]
    public async Task property_level_transform_without_transformer_throws()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.PropertyTransformOrders.Add(new PropertyTransformOrder { Id = Guid.NewGuid(), Secret = "top-secret" });
            var sut = _CreateSut();

            // when
            var act = () => _Capture(sut, db);

            // then
            act.Should()
                .Throw<OptionsValidationException>()
                .WithMessage("*SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform.*");
        }
    }

    [Fact]
    public async Task non_audit_tracked_entity_skipped_in_opt_in_mode()
    {
        // given - Product does not implement IAuditTracked
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var product = new Product { Id = Guid.NewGuid(), Name = "Widget" };
            db.Products.Add(product);

            var sut = _CreateSut(); // AuditByDefault = false (default)

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task all_entities_mode_captures_non_marker_entities()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var product = new Product { Id = Guid.NewGuid(), Name = "Widget" };
            db.Products.Add(product);

            var sut = _CreateSut(opts => opts.AuditByDefault = true);

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].EntityType.Should().Contain(nameof(Product));
        }
    }

    [Fact]
    public async Task audit_ignore_class_excluded_in_all_entities_mode()
    {
        // given - InternalLog has [AuditIgnore] on the class
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var log = new InternalLog { Id = Guid.NewGuid(), Message = "internal" };
            db.InternalLogs.Add(log);

            var sut = _CreateSut(opts => opts.AuditByDefault = true);

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task entity_filter_excludes_entity_type()
    {
        // given - filter excludes Order
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
            };
            db.Orders.Add(order);

            var sut = _CreateSut(opts => opts.EntityFilter = t => t == typeof(Order));

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task property_filter_excludes_property()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
            };
            db.Orders.Add(order);

            var sut = _CreateSut(opts => opts.PropertyFilter = (_, name) => name == "CustomerName");

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].NewValues.Should().NotContainKey("CustomerName");
        }
    }

    [Fact]
    public async Task owned_entity_inherits_auditability_from_owner()
    {
        // given - Customer (IAuditTracked) owns Address
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                Name = "Alice",
                Address = new Address { Street = "Main St", City = "Springfield" },
            };
            db.Customers.Add(customer);

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then - should capture both Customer and the owned Address
            result.Should().NotBeEmpty();
            var addressEntry = result.FirstOrDefault(e =>
                e.EntityType != null && e.EntityType.Contains(nameof(Address))
            );
            addressEntry.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task owned_entity_update_captured_when_owner_is_audit_tracked()
    {
        // given - Customer (IAuditTracked) owns Address; modifying Address should emit an audit entry.
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                Id = customerId,
                Name = "Alice",
                Address = new Address { Street = "Main", City = "Town" },
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            customer.Address.Street = "New St";
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut(); // opt-in mode

            // when
            var result = _Capture(sut, db);

            // then - address update is captured because owner (Customer) is IAuditTracked
            result.Should().HaveCount(1);
            var addressEntry = result[0];
            addressEntry.EntityType.Should().Contain(nameof(Address));
            addressEntry.EntityId.Should().Be(customerId.ToString());
        }
    }

    [Fact]
    public async Task soft_delete_produces_soft_deleted_action()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
                IsDeleted = false,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // when
            order.IsDeleted = true;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].Action.Should().Be(AuditActionNames.SoftDeleted);
        }
    }

    [Fact]
    public async Task restore_produces_restored_action()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
                IsDeleted = true,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // when
            order.IsDeleted = false;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].Action.Should().Be(AuditActionNames.Restored);
        }
    }

    [Fact]
    public async Task suspend_produces_suspended_action()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
                IsSuspended = false,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // when
            order.IsSuspended = true;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].Action.Should().Be(AuditActionNames.Suspended);
        }
    }

    [Fact]
    public async Task unsuspend_produces_unsuspended_action()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
                IsSuspended = true,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            // when
            order.IsSuspended = false;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].Action.Should().Be(AuditActionNames.Unsuspended);
        }
    }

    [Fact]
    public async Task audit_log_entry_class_not_captured_in_all_entities_mode()
    {
        // given - AuditLogEntry has [AuditIgnore] to prevent recursive capture
        // We can't add AuditLogEntry to TestDbContext directly (different assembly), but
        // we verify via InternalLog which also has [AuditIgnore].
        // (This test is a duplicate of audit_ignore_class_excluded_in_all_entities_mode for AuditLogEntry specifically,
        // but since AuditLogEntry is not in our test DbContext, we verify the mechanism via InternalLog.)
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var log = new InternalLog { Id = Guid.NewGuid(), Message = "audit log entry" };
            db.InternalLogs.Add(log);

            var sut = _CreateSut(opts => opts.AuditByDefault = true);

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task is_enabled_false_returns_empty_and_logs_warning_once()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
            };
            db.Orders.Add(order);

            var logger = Substitute.For<ILogger<EfAuditChangeCapture>>();
            var sut = _CreateSut(opts => opts.IsEnabled = false, logger);

            // when
            var result = _Capture(sut, db);
            var secondResult = _Capture(sut, db);

            // then
            result.Should().BeEmpty();
            secondResult.Should().BeEmpty();
            logger.ReceivedCalls().Should().ContainSingle(call => _IsDisabledAuditWarningLog(call));
        }
    }

    [Fact]
    public async Task framework_managed_properties_excluded_by_default()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new FrameworkManagedOrder
            {
                Id = Guid.NewGuid(),
                DateCreated = DateTimeOffset.UtcNow,
                Name = "Alice",
            };
            db.FrameworkManagedOrders.Add(order);

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            var entry = result[0];
            entry.NewValues.Should().ContainKey("Name");
            entry.NewValues.Should().NotContainKey("DateCreated");
        }
    }

    [Fact]
    public async Task framework_managed_property_can_be_reincluded_via_default_excluded_properties()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new FrameworkManagedOrder
            {
                Id = Guid.NewGuid(),
                DateCreated = DateTimeOffset.UtcNow,
                Name = "Alice",
            };
            db.FrameworkManagedOrders.Add(order);

            var sut = _CreateSut(opts => opts.DefaultExcludedProperties.Remove("DateCreated"));

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].NewValues.Should().ContainKey("DateCreated");
        }
    }

    [Fact]
    public async Task entity_filter_result_cached_per_type()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var filterCalls = 0;
            var sut = _CreateSut(opts =>
                opts.EntityFilter = type =>
                {
                    filterCalls++;
                    return type == typeof(Product);
                }
            );

            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "Alice",
                    Email = "a@b.com",
                    Phone = "555",
                }
            );
            _ = _Capture(sut, db);
            await db.SaveChangesAsync();

            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "Bob",
                    Email = "b@b.com",
                    Phone = "666",
                }
            );
            _ = _Capture(sut, db);

            // then
            filterCalls.Should().Be(1);
        }
    }

    [Fact]
    public async Task property_filter_result_cached_per_property()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var filterCalls = 0;
            var sut = _CreateSut(opts =>
                opts.PropertyFilter = (_, _) =>
                {
                    filterCalls++;
                    return false;
                }
            );

            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "Alice",
                    Email = "a@b.com",
                    Phone = "555",
                }
            );
            _ = _Capture(sut, db);
            await db.SaveChangesAsync();

            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "Bob",
                    Email = "b@b.com",
                    Phone = "666",
                }
            );
            _ = _Capture(sut, db);

            // then
            filterCalls.Should().Be(7);
        }
    }

    [Fact]
    public async Task transformer_exception_falls_back_to_redact_and_logs_warning()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var logger = Substitute.For<ILogger<EfAuditChangeCapture>>();
            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "Alice",
                    Email = "a@b.com",
                    Phone = "555",
                }
            );
            var sut = _CreateSut(
                opts =>
                {
                    opts.SensitiveDataStrategy = SensitiveDataStrategy.Transform;
                    opts.SensitiveValueTransformer = _ => throw new InvalidOperationException("boom");
                },
                logger
            );

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].NewValues.Should().ContainKey("Email").WhoseValue.Should().Be("***");
            logger.ReceivedCalls().Should().ContainSingle(call => _IsTransformerWarningLog(call));
        }
    }

    [Fact]
    public async Task composite_keys_are_serialized_as_json_arrays()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.CompositeKeyOrders.Add(
                new CompositeKeyOrder
                {
                    TenantId = "tenant,a",
                    OrderId = "order,1",
                    Name = "Alice",
                }
            );
            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().HaveCount(1);
            result[0].EntityId.Should().Be("[\"tenant,a\",\"order,1\"]");
        }
    }

    [Fact]
    public async Task non_entity_entry_objects_in_entries_are_silently_skipped()
    {
        // given - non-EntityEntry objects mixed with valid entries are silently skipped.
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Alice",
                Email = "a@b.com",
                Phone = "555",
            };
            db.Orders.Add(order);

            var sut = _CreateSut();

            // inject non-EntityEntry objects alongside real entries
            var validEntries = db.ChangeTracker.Entries().Select(e => (object)e);
            var mixedEntries = validEntries.Concat(["not-an-entity-entry", 42]);
            var result = sut.CaptureChanges(mixedEntries, _UserId, _AccountId, _TenantId, _CorrelationId, _Timestamp);

            // then - non-EntityEntry objects are silently skipped; valid entry is captured
            result.Should().HaveCount(1);
            result[0].Action.Should().Be(AuditActionNames.Created);
        }
    }

    private static bool _IsDisabledAuditWarningLog(ICall call)
    {
        var arguments = call.GetArguments();
        return arguments.Length == 5
            && arguments[0] is LogLevel.Warning
            && arguments[2]?.ToString()?.Contains("Audit logging is disabled", StringComparison.Ordinal) == true;
    }

    private static bool _IsTransformerWarningLog(ICall call)
    {
        var arguments = call.GetArguments();
        return arguments.Length == 5
            && arguments[0] is LogLevel.Warning
            && arguments[2]?.ToString()?.Contains("Sensitive value transformer threw", StringComparison.Ordinal)
                == true;
    }
}
