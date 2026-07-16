// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
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

public class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";

    public string Email { get; set; } = "";

    public string Phone { get; set; } = "";

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

public class InternalLog
{
    public Guid Id { get; set; }
    public string Message { get; set; } = "";
}

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Address Address { get; set; } = new();
}

public class PropertyTransformOrder
{
    public Guid Id { get; set; }

    public string Secret { get; set; } = "";
}

public class FrameworkManagedOrder
{
    public Guid Id { get; set; }
    public DateTimeOffset DateCreated { get; set; }
    public string Name { get; set; } = "";
}

public class CompositeKeyOrder
{
    public string TenantId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string Name { get; set; } = "";
}

public class GeneratedKeyOrder
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public AddressDetails Details { get; set; } = new();
}

public class AddressDetails
{
    public string Secret { get; set; } = "";
}

public abstract class AuditedBaseEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public OwnedAuditDetails? Details { get; set; }
}

public sealed class InheritedAuditEntity : AuditedBaseEntity { }

public sealed class ExcludedDerivedAuditEntity : AuditedBaseEntity { }

public sealed class OwnedAuditDetails
{
    public string Value { get; set; } = "";
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
    public DbSet<GeneratedKeyOrder> GeneratedKeyOrders => Set<GeneratedKeyOrder>();
    public DbSet<InheritedAuditEntity> InheritedAuditEntities => Set<InheritedAuditEntity>();
    public DbSet<ExcludedDerivedAuditEntity> ExcludedDerivedAuditEntities => Set<ExcludedDerivedAuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        order.IsAudited();
        order.Property(e => e.Email).IsAuditSensitive();
        order.Property(e => e.Phone).IsAuditSensitive(SensitiveDataStrategy.Exclude);
        order.Property(e => e.LastComputedAt).ExcludeFromAudit();
        order.Property(e => e.Id).ValueGeneratedNever();

        var customer = modelBuilder.Entity<Customer>();
        customer.IsAudited();
        customer.OwnsOne(
            c => c.Address,
            address =>
            {
                address.Property(a => a.City).IsAuditSensitive().ExcludeFromAudit();
                address.OwnsOne(a => a.Details, details => details.Property(d => d.Secret).IsAuditSensitive());
            }
        );

        modelBuilder.Entity<InternalLog>().ExcludeFromAudit();
        modelBuilder.Entity<PropertyTransformOrder>().IsAudited();
        modelBuilder
            .Entity<PropertyTransformOrder>()
            .Property(e => e.Secret)
            .IsAuditSensitive(SensitiveDataStrategy.Transform);
        modelBuilder.Entity<FrameworkManagedOrder>().IsAudited();
        modelBuilder.Entity<CompositeKeyOrder>().IsAudited();
        modelBuilder.Entity<GeneratedKeyOrder>().IsAudited();

        modelBuilder.Entity<AuditedBaseEntity>().IsAudited();
        modelBuilder.Entity<AuditedBaseEntity>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<AuditedBaseEntity>().OwnsOne(e => e.Details);
        modelBuilder.Entity<InheritedAuditEntity>();
        modelBuilder.Entity<ExcludedDerivedAuditEntity>().ExcludeFromAudit();

        modelBuilder.Entity<Product>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<InternalLog>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<Customer>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<PropertyTransformOrder>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<FrameworkManagedOrder>().Property(e => e.Id).ValueGeneratedNever();
        modelBuilder.Entity<CompositeKeyOrder>().HasKey(e => new { e.TenantId, e.OrderId });
        modelBuilder.Entity<GeneratedKeyOrder>().Property(e => e.Id).ValueGeneratedOnAdd();
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
#pragma warning disable MA0045 // Do not use blocking calls, even when the calling method must become async
        db.Database.EnsureCreated();
#pragma warning restore MA0045
        return (db, conn);
    }

    private static EfAuditChangeCapture _CreateSut(
        Action<AuditLogOptions>? configure = null,
        ILogger<EfAuditChangeCapture>? logger = null
    )
    {
        var opts = new AuditLogOptions();
        configure?.Invoke(opts);
        logger ??= Substitute.For<ILogger<EfAuditChangeCapture>>();
        // LoggerMessage source-generated wrappers short-circuit on IsEnabled,
        // which an unconfigured substitute returns false for. Force-enable so
        // tests asserting ReceivedCalls() actually see the underlying Log call.
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        return new EfAuditChangeCapture(Options.Create(opts), logger);
    }

    private static IReadOnlyList<AuditLogEntryData> _Capture(EfAuditChangeCapture sut, DbContext db)
    {
        var entries = db.ChangeTracker.Entries().Cast<object>();
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
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

            // when
            order.CustomerName = "Bob";
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

            // when
            db.Orders.Remove(order);

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

            order.CustomerName = "Bob";
            order.LastComputedAt = DateTime.UtcNow.AddMinutes(1);
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
            var entry = result[0];
            entry.ChangedFields.Should().NotContain("LastComputedAt");
            entry.OldValues.Should().NotContainKey("LastComputedAt");
            entry.NewValues.Should().NotContainKey("LastComputedAt");
        }
    }

    [Fact]
    public async Task audit_sensitive_redact_replaces_value_with_stars()
    {
        // given - Email uses sensitive metadata with the default Redact strategy
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
            result.Should().ContainSingle();
            var entry = result[0];
            entry.NewValues.Should().ContainKey("Email").WhoseValue.Should().Be("***");
        }
    }

    [Fact]
    public async Task audit_sensitive_exclude_omits_property_entirely()
    {
        // given - Phone uses a property-specific Exclude strategy
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
            result.Should().ContainSingle();
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
            result.Should().ContainSingle();
            var entry = result[0];
            // Email has no property-specific strategy, so it falls back to global Transform
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
        // given - Product has no explicit audit policy
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
    public async Task audit_by_default_captures_entity_without_explicit_policy()
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
            result.Should().ContainSingle();
            result[0].EntityType.Should().Contain(nameof(Product));
        }
    }

    [Fact]
    public async Task audit_ignore_class_excluded_in_all_entities_mode()
    {
        // given - InternalLog has explicit exclusion metadata
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
    public async Task finalized_model_exposes_primitive_audit_policy_annotations()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = db.Model.FindEntityType(typeof(Order));
            var product = db.Model.FindEntityType(typeof(Product));
            var internalLog = db.Model.FindEntityType(typeof(InternalLog));

            order.Should().NotBeNull();
            product.Should().NotBeNull();
            internalLog.Should().NotBeNull();

            order!.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited)?.Value.Should().Be(true);
            product!.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited).Should().BeNull();
            internalLog!.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited)?.Value.Should().Be(false);

            order
                .FindProperty(nameof(Order.LastComputedAt))!
                .FindAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsExcluded)
                ?.Value.Should()
                .Be(true);
            order
                .FindProperty(nameof(Order.Email))!
                .FindAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsSensitive)
                ?.Value.Should()
                .Be(true);
            order
                .FindProperty(nameof(Order.Phone))!
                .FindAnnotation(HeadlessAuditPolicyAnnotations.PropertySensitiveStrategy)
                ?.Value.Should()
                .Be((int)SensitiveDataStrategy.Exclude);
        }
    }

    [Fact]
    public async Task derived_entity_inherits_nearest_base_policy_and_can_override_it()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.InheritedAuditEntities.Add(new InheritedAuditEntity { Id = Guid.NewGuid(), Name = "included" });
            db.ExcludedDerivedAuditEntities.Add(
                new ExcludedDerivedAuditEntity { Id = Guid.NewGuid(), Name = "excluded" }
            );

            var result = _Capture(_CreateSut(), db);

            result.Should().ContainSingle();
            result[0].EntityType.Should().Be(typeof(InheritedAuditEntity).FullName);
        }
    }

    [Fact]
    public async Task owned_entry_uses_derived_owner_policy_override()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.ExcludedDerivedAuditEntities.Add(
                new ExcludedDerivedAuditEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "excluded",
                    Details = new OwnedAuditDetails { Value = "private" },
                }
            );

            var result = _Capture(_CreateSut(), db);

            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task owned_entry_entity_filter_uses_derived_owner_type()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.InheritedAuditEntities.Add(
                new InheritedAuditEntity
                {
                    Id = Guid.NewGuid(),
                    Name = "filtered",
                    Details = new OwnedAuditDetails { Value = "private" },
                }
            );

            var result = _Capture(
                _CreateSut(opts => opts.EntityFilter = type => type == typeof(InheritedAuditEntity)),
                db
            );

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

            var sut = _CreateSut(opts =>
                opts.PropertyFilter = (_, name) => string.Equals(name, "CustomerName", StringComparison.Ordinal)
            );

            // when
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
            result[0].NewValues.Should().NotContainKey("CustomerName");
        }
    }

    [Fact]
    public async Task owned_entity_inherits_auditability_from_owner()
    {
        // given - the explicitly audited Customer owns Address
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
                e.EntityType?.Contains(nameof(Address), StringComparison.Ordinal) == true
            );
            addressEntry.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task owned_entity_update_captured_when_owner_is_audit_tracked()
    {
        // given - Customer is explicitly audited; modifying Address should emit an audit entry.
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
            await db.SaveChangesAsync(AbortToken);

            customer.Address.Street = "New St";
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut(); // opt-in mode

            // when
            var result = _Capture(sut, db);

            // then - address update is captured because its root owner is audited
            result.Should().ContainSingle();
            var addressEntry = result[0];
            addressEntry.EntityType.Should().Contain(nameof(Address));
            addressEntry.EntityId.Should().Be(customerId.ToString());
        }
    }

    [Fact]
    public async Task nested_owned_entity_inherits_root_policy_and_uses_local_sensitive_metadata()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.Customers.Add(
                new Customer
                {
                    Id = Guid.NewGuid(),
                    Name = "Alice",
                    Address = new Address
                    {
                        Street = "Main",
                        City = "Town",
                        Details = new AddressDetails { Secret = "private" },
                    },
                }
            );

            var result = _Capture(_CreateSut(), db);

            var detailsEntry = result.Single(entry =>
                entry.EntityType?.Contains(nameof(AddressDetails), StringComparison.Ordinal) == true
            );
            detailsEntry.NewValues.Should().ContainKey(nameof(AddressDetails.Secret)).WhoseValue.Should().Be("***");
        }
    }

    [Fact]
    public async Task property_exclusion_wins_over_sensitive_metadata_on_owned_property()
    {
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                Name = "Alice",
                Address = new Address { Street = "Main", City = "Town" },
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(AbortToken);

            customer.Address.City = "Elsewhere";
            db.ChangeTracker.DetectChanges();

            var result = _Capture(_CreateSut(), db);

            result.Should().BeEmpty();
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
            await db.SaveChangesAsync(AbortToken);

            // when
            order.IsDeleted = true;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

            // when
            order.IsDeleted = false;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

            // when
            order.IsSuspended = true;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

            // when
            order.IsSuspended = false;
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();
            var result = _Capture(sut, db);

            // then
            result.Should().ContainSingle();
            result[0].Action.Should().Be(AuditActionNames.Unsuspended);
        }
    }

    [Fact]
    public async Task audit_log_entry_class_not_captured_in_all_entities_mode()
    {
        // given - use the real storage entity and its finalized model configuration.
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            db.Add(
                new AuditLogEntry
                {
                    Id = 1,
                    CreatedAt = _Timestamp.UtcDateTime,
                    Action = "audit.created",
                }
            );

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
            result.Should().ContainSingle();
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
            result.Should().ContainSingle();
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
            await db.SaveChangesAsync(AbortToken);

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
            await db.SaveChangesAsync(AbortToken);

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
            result.Should().ContainSingle();
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
            result.Should().ContainSingle();
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
            var validEntries = db.ChangeTracker.Entries().Cast<object>();
            var mixedEntries = validEntries.Concat(["not-an-entity-entry", 42]);
            var result = sut.CaptureChanges(mixedEntries, _UserId, _AccountId, _TenantId, _CorrelationId, _Timestamp);

            // then - non-EntityEntry objects are silently skipped; valid entry is captured
            result.Should().ContainSingle();
            result[0].Action.Should().Be(AuditActionNames.Created);
        }
    }

    [Fact]
    public async Task resolve_entity_ids_patches_temporary_store_generated_values_after_save()
    {
        // given - a store-generated key holds an EF temporary value at capture time
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            var order = new GeneratedKeyOrder { Name = "gen" };
            db.GeneratedKeyOrders.Add(order);

            var sut = _CreateSut();
            var result = _Capture(sut, db);
            result.Should().ContainSingle();

            // when
            await db.SaveChangesAsync(AbortToken);
            sut.ResolveEntityIds(result);

            // then - both EntityId and the captured Id value reflect the real post-save key
            order.Id.Should().BePositive();
            result[0].EntityId.Should().Be(order.Id.ToString(CultureInfo.InvariantCulture));
            result[0].NewValues.Should().ContainKey("Id").WhoseValue.Should().Be(order.Id);
        }
    }

    [Fact]
    public async Task resolve_entity_ids_resolves_passed_entries_when_another_capture_interleaves()
    {
        // given - one scoped capture instance shared by two contexts (e.g. a domain-event
        // handler saving a second DbContext mid-save must not clobber the outer capture's state)
        var (db, conn) = _CreateDb();
        var (db2, conn2) = _CreateDb();
        await using (conn)
        await using (db)
        await using (conn2)
        await using (db2)
        {
            var order = new GeneratedKeyOrder { Name = "outer" };
            db.GeneratedKeyOrders.Add(order);

            var sut = _CreateSut();
            var outerResult = _Capture(sut, db);
            outerResult.Should().ContainSingle();

            // when - an interleaved capture for a second context runs before the outer resolve
            db2.GeneratedKeyOrders.Add(new GeneratedKeyOrder { Name = "inner" });
            _ = _Capture(sut, db2);

            await db.SaveChangesAsync(AbortToken);
            sut.ResolveEntityIds(outerResult);

            // then - the outer entries still resolve to their real post-save values
            outerResult[0].EntityId.Should().Be(order.Id.ToString(CultureInfo.InvariantCulture));
            outerResult[0].NewValues.Should().ContainKey("Id").WhoseValue.Should().Be(order.Id);
        }
    }

    [Fact]
    public async Task capture_error_strategy_continue_skips_failing_entity_and_keeps_others()
    {
        // given - the entity filter throws for one entity type only
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "ok",
                    Email = "a@b.com",
                    Phone = "555",
                }
            );
            db.Customers.Add(new Customer { Id = Guid.NewGuid(), Name = "boom" });

            var sut = _CreateSut(options =>
                options.EntityFilter = type =>
                    type == typeof(Customer) ? throw new InvalidOperationException("boom") : false
            );

            // when
            var result = _Capture(sut, db);

            // then - the failing entity's entry is skipped; the healthy entity is still captured
            result.Should().ContainSingle(e => e.EntityType == typeof(Order).FullName);
        }
    }

    [Fact]
    public async Task capture_error_strategy_throw_propagates_per_entity_capture_failure()
    {
        // given
        var (db, conn) = _CreateDb();
        await using (conn)
        await using (db)
        {
            db.Orders.Add(
                new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerName = "ok",
                    Email = "a@b.com",
                    Phone = "555",
                }
            );

            var sut = _CreateSut(options =>
            {
                options.CaptureErrorStrategy = CaptureErrorStrategy.Throw;
                options.EntityFilter = _ => throw new InvalidOperationException("boom");
            });

            // when
            var act = () => _Capture(sut, db);

            // then - the per-entity failure aborts the capture instead of being swallowed
            act.Should().Throw<InvalidOperationException>().WithMessage("boom");
        }
    }

    [Fact]
    public async Task updated_entity_with_only_ignored_property_change_produces_no_entry()
    {
        // given - LastComputedAt is excluded in model metadata and is the only modified property
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
            await db.SaveChangesAsync(AbortToken);

            order.LastComputedAt = DateTime.UtcNow.AddMinutes(5);
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then - an update whose only changes are non-audited yields no audit entry at all
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task updated_entity_with_only_excluded_sensitive_property_change_produces_no_entry()
    {
        // given - Phone has a property-specific sensitive Exclude strategy and is the only modified property
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
            await db.SaveChangesAsync(AbortToken);

            order.Phone = "555-new";
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then - the excluded property produces no changed field, so the update is suppressed
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task audit_sensitive_redact_on_update_masks_old_and_new_values_but_keeps_changed_field()
    {
        // given - Email uses sensitive metadata with the default Redact strategy
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
            await db.SaveChangesAsync(AbortToken);

            order.Email = "bob@secret.com";
            db.ChangeTracker.DetectChanges();

            var sut = _CreateSut();

            // when
            var result = _Capture(sut, db);

            // then - the change is recorded but both values are masked
            result.Should().ContainSingle();
            var entry = result[0];
            entry.ChangedFields.Should().Contain("Email");
            entry.OldValues.Should().ContainKey("Email").WhoseValue.Should().Be("***");
            entry.NewValues.Should().ContainKey("Email").WhoseValue.Should().Be("***");
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
