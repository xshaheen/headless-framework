// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class ComplexCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public ShippingAddress Shipping { get; set; } = new();
    public ContactCard Contact { get; set; } = new();
    public ContactCard? Backup { get; set; }
}

public record ShippingAddress
{
    public string Street { get; init; } = "";
    public string City { get; init; } = "";
    public GeoPoint Geo { get; init; } = new();
}

public record GeoPoint
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public record ContactCard
{
    public string Phone { get; init; } = "";
    public string Note { get; init; } = "";
}

public class ComplexDbContext(DbContextOptions<ComplexDbContext> options) : DbContext(options)
{
    public DbSet<ComplexCustomer> Customers => Set<ComplexCustomer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var customer = modelBuilder.Entity<ComplexCustomer>();
        customer.IsAudited();
        customer.Property(e => e.Id).ValueGeneratedNever();
        customer.ComplexProperty(e => e.Shipping, shipping => shipping.ComplexProperty(s => s.Geo));
        customer.ComplexProperty(
            e => e.Contact,
            contact =>
            {
                contact.Property(c => c.Phone).IsAuditSensitive(SensitiveDataStrategy.Redact);
                contact.Property(c => c.Note).ExcludeFromAudit();
            }
        );
        customer.ComplexProperty(e => e.Backup);
    }
}

public sealed partial class EfAuditChangeCaptureTests
{
    private static (ComplexDbContext db, SqliteConnection conn) _CreateComplexDb()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        var db = new ComplexDbContext(new DbContextOptionsBuilder<ComplexDbContext>().UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return (db, conn);
    }

    private static async Task<ComplexCustomer> _SeedComplexCustomerAsync(ComplexDbContext db)
    {
        var customer = new ComplexCustomer
        {
            Id = Guid.NewGuid(),
            Name = "Alice",
            Shipping = new ShippingAddress
            {
                Street = "1 Nile St",
                City = "Cairo",
                Geo = new GeoPoint { Latitude = 30.04, Longitude = 31.23 },
            },
            Contact = new ContactCard { Phone = "555-0100", Note = "call after 5" },
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(AbortToken);

        return customer;
    }

    [Fact]
    public async Task should_capture_an_update_when_only_a_complex_property_changed()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            var customer = await _SeedComplexCustomerAsync(db);

            // when
            customer.Shipping = customer.Shipping with
            {
                City = "Giza",
            };
            db.ChangeTracker.DetectChanges();
            var result = _Capture(_CreateSut(), db);

            // then
            var entry = result.Should().ContainSingle().Subject;
            entry.ChangeType.Should().Be(AuditChangeType.Updated);
            entry.ChangedFields.Should().Equal("Shipping.City");
            entry.OldValues.Should().ContainKey("Shipping.City").WhoseValue.Should().Be("Cairo");
            entry.NewValues.Should().ContainKey("Shipping.City").WhoseValue.Should().Be("Giza");
        }
    }

    [Fact]
    public async Task should_record_a_nested_complex_property_under_its_full_path()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            var customer = await _SeedComplexCustomerAsync(db);

            // when
            customer.Shipping = customer.Shipping with
            {
                Geo = customer.Shipping.Geo with { Latitude = 29.97 },
            };
            db.ChangeTracker.DetectChanges();
            var result = _Capture(_CreateSut(), db);

            // then
            var entry = result.Should().ContainSingle().Subject;
            entry.ChangedFields.Should().Equal("Shipping.Geo.Latitude");
            entry.NewValues.Should().ContainKey("Shipping.Geo.Latitude").WhoseValue.Should().Be(29.97);
        }
    }

    [Fact]
    public async Task should_skip_an_update_when_a_complex_property_is_replaced_with_equal_values()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            var customer = await _SeedComplexCustomerAsync(db);

            // when
            customer.Shipping = customer.Shipping with
            { };
            db.ChangeTracker.DetectChanges();
            var result = _Capture(_CreateSut(), db);

            // then
            result.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task should_capture_complex_property_values_on_create()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            db.Customers.Add(
                new ComplexCustomer
                {
                    Id = Guid.NewGuid(),
                    Name = "Alice",
                    Shipping = new ShippingAddress { City = "Cairo" },
                    Contact = new ContactCard { Phone = "555-0100", Note = "private" },
                }
            );

            // when
            var result = _Capture(_CreateSut(), db);

            // then
            var newValues = result.Should().ContainSingle().Subject.NewValues;
            newValues.Should().ContainKey("Shipping.City").WhoseValue.Should().Be("Cairo");
            newValues.Should().ContainKey("Shipping.Geo.Longitude");
            newValues.Should().ContainKey("Contact.Phone").WhoseValue.Should().Be("***");
            newValues.Should().NotContainKey("Contact.Note");
        }
    }

    [Fact]
    public async Task should_apply_sensitive_and_excluded_policy_to_complex_type_properties_on_update()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            var customer = await _SeedComplexCustomerAsync(db);

            // when
            customer.Contact = new ContactCard { Phone = "555-0199", Note = "changed" };
            db.ChangeTracker.DetectChanges();
            var result = _Capture(_CreateSut(), db);

            // then
            var entry = result.Should().ContainSingle().Subject;
            entry.ChangedFields.Should().Equal("Contact.Phone");
            entry.OldValues.Should().ContainKey("Contact.Phone").WhoseValue.Should().Be("***");
            entry.NewValues.Should().ContainKey("Contact.Phone").WhoseValue.Should().Be("***");
        }
    }

    [Fact]
    public async Task should_pass_the_dotted_path_to_the_property_filter()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            var customer = await _SeedComplexCustomerAsync(db);
            var seen = new List<string>();

            // when
            customer.Shipping = customer.Shipping with
            {
                City = "Giza",
                Street = "2 Nile St",
            };
            db.ChangeTracker.DetectChanges();
            var result = _Capture(
                _CreateSut(options =>
                    options.PropertyFilter = (_, name) =>
                    {
                        seen.Add(name);
                        return string.Equals(name, "Shipping.Street", StringComparison.Ordinal);
                    }
                ),
                db
            );

            // then
            seen.Should().Contain(["Shipping.City", "Shipping.Street", "Shipping.Geo.Latitude"]);
            result.Should().ContainSingle().Subject.ChangedFields.Should().Equal("Shipping.City");
        }
    }

    [Fact]
    public async Task should_capture_an_optional_complex_property_that_gains_a_value()
    {
        // given
        var (db, conn) = _CreateComplexDb();
        await using (conn)
        await using (db)
        {
            var customer = await _SeedComplexCustomerAsync(db);

            // when
            customer.Backup = new ContactCard { Phone = "555-0142" };
            db.ChangeTracker.DetectChanges();
            var result = _Capture(_CreateSut(), db);

            // then
            var entry = result.Should().ContainSingle().Subject;
            entry.ChangedFields.Should().Contain("Backup.Phone");
            entry.OldValues.Should().ContainKey("Backup.Phone").WhoseValue.Should().BeNull();
            entry.NewValues.Should().ContainKey("Backup.Phone").WhoseValue.Should().Be("555-0142");
        }
    }
}
