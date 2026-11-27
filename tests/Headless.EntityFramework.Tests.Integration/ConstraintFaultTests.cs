// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Primitives;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Proves on each engine that a refused write is classified by its real code: a foreign key (missing row, RESTRICT,
/// and NO ACTION), a CHECK constraint, a NOT NULL column, a value too long, and a numeric overflow, and that
/// <c>SaveChangesOrConflictAsync</c> answers a refused delete of a referenced row with the caller's own conflict.
/// </summary>
public abstract class ConstraintFaultTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IConstraintFaultFixture
{
    private static readonly ErrorDescriptor _CategoryInUse = new("catalog:category_in_use", "The category is in use.");

    [Fact]
    public async Task should_name_the_foreign_key_when_a_row_references_a_missing_row()
    {
        await using var db = fixture.CreateContext();
        db.Products.Add(_NewProduct(categoryId: Guid.NewGuid()));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(fixture.ReportsConstraintNames ? ConstraintFaultDbContext.ProductCategoryKey : null);
        RelationalFaults.IsCheckViolation(exception).Should().BeFalse();
        RelationalFaults.IsUniqueViolation(exception).Should().BeFalse();
    }

    [Fact]
    public async Task should_name_the_restricting_foreign_key_when_a_delete_removes_a_referenced_row()
    {
        var category = await _InsertCategoryAsync();
        await _InsertAsync(_NewProduct(category.Id));

        await using var db = fixture.CreateContext();
        db.Categories.Remove(await db.Categories.SingleAsync(x => x.Id == category.Id, AbortToken));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(fixture.ReportsConstraintNames ? ConstraintFaultDbContext.ProductCategoryKey : null);
    }

    [Fact]
    public async Task should_name_the_no_action_foreign_key_when_a_delete_removes_a_referenced_row()
    {
        var category = await _InsertCategoryAsync();
        await _InsertAsync(new Tag { CategoryId = category.Id });

        await using var db = fixture.CreateContext();
        db.Categories.Remove(await db.Categories.SingleAsync(x => x.Id == category.Id, AbortToken));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(fixture.ReportsConstraintNames ? ConstraintFaultDbContext.TagCategoryKey : null);
    }

    [Fact]
    public async Task should_throw_the_mapped_conflict_when_a_delete_removes_a_row_the_named_key_references()
    {
        // skip: SQLite reports no constraint name to map.
        Assert.SkipUnless(fixture.ReportsConstraintNames, "SQLite reports no constraint name to map.");

        var category = await _InsertCategoryAsync();
        await _InsertAsync(_NewProduct(category.Id));

        await using var db = fixture.CreateContext();
        db.Categories.Remove(await db.Categories.SingleAsync(x => x.Id == category.Id, AbortToken));

        var save = async () =>
            await db.SaveChangesOrConflictAsync(
                ConstraintFaultDbContext.ProductCategoryKey,
                _CategoryInUse,
                AbortToken
            );

        var conflict = (await save.Should().ThrowAsync<ConflictException>()).Which;
        conflict.Errors.Should().ContainSingle().Which.Should().BeSameAs(_CategoryInUse);
        conflict.InnerException.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task should_name_the_check_constraint_when_a_row_breaks_it()
    {
        var category = await _InsertCategoryAsync();

        await using var db = fixture.CreateContext();
        db.Products.Add(_NewProduct(category.Id, stock: -1));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsCheckViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(fixture.ReportsConstraintNames ? ConstraintFaultDbContext.StockCheck : null);
        RelationalFaults.IsReferenceViolation(exception).Should().BeFalse();
    }

    [Fact]
    public async Task should_recognize_a_null_written_to_a_required_column()
    {
        var category = await _InsertCategoryAsync();

        await using var db = fixture.CreateContext();
        db.Products.Add(_NewProduct(category.Id, name: null!));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsNotNullViolation(exception).Should().BeTrue();
        RelationalFaults.IsCheckViolation(exception).Should().BeFalse();
    }

    [Fact]
    public async Task should_recognize_a_value_longer_than_its_column()
    {
        // skip: SQLite stores a value of any length in any column.
        Assert.SkipUnless(fixture.EnforcesColumnTypes, "SQLite stores a value of any length in any column.");

        var category = await _InsertCategoryAsync();

        await using var db = fixture.CreateContext();
        db.Products.Add(_NewProduct(category.Id, name: new string('x', ConstraintFaultDbContext.NameLength + 1)));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsMaxLengthExceeded(exception).Should().BeTrue();
        RelationalFaults.IsNumericOverflow(exception).Should().BeFalse();
    }

    protected async Task<Category> _InsertCategoryAsync()
    {
        var category = new Category();
        await _InsertAsync(category);

        return category;
    }

    protected async Task _InsertAsync(object entity)
    {
        await using var db = fixture.CreateContext();
        db.Add(entity);
        await db.SaveChangesAsync(AbortToken);
    }

    protected static async Task<DbUpdateException> _SaveFailureAsync(ConstraintFaultDbContext db)
    {
        var save = () => db.SaveChangesAsync(AbortToken);

        return (await save.Should().ThrowAsync<DbUpdateException>()).Which;
    }

    protected static Product _NewProduct(Guid categoryId, string name = "widget", decimal price = 1, int stock = 0) =>
        new()
        {
            CategoryId = categoryId,
            Name = name,
            Price = price,
            Stock = stock,
        };

    protected TFixture Fixture => fixture;
}

public interface IConstraintFaultFixture
{
    /// <summary>Whether the engine reports the violated constraint's name (PostgreSQL and SQL Server do, SQLite does not).</summary>
    bool ReportsConstraintNames { get; }

    /// <summary>Whether the engine refuses a string longer than its column allows (SQLite does not).</summary>
    bool EnforcesColumnTypes { get; }

    ConstraintFaultDbContext CreateContext();
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CategoryId { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }
}

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CategoryId { get; set; }
}

public sealed class ConstraintFaultDbContext(DbContextOptions<ConstraintFaultDbContext> options) : DbContext(options)
{
    public const string ProductCategoryKey = "FK_ConstraintFaultProducts_Category";
    public const string TagCategoryKey = "FK_ConstraintFaultTags_Category";
    public const string StockCheck = "CK_ConstraintFaultProducts_Stock";
    public const int NameLength = 16;

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>().ToTable("ConstraintFaultCategories");

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("ConstraintFaultProducts", table => table.HasCheckConstraint(StockCheck, "\"Stock\" >= 0"));
            product.Property(x => x.Name).HasMaxLength(NameLength);
            product.Property(x => x.Price).HasPrecision(5, 2);
            product
                .HasOne<Category>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .HasConstraintName(ProductCategoryKey)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tag>(tag =>
        {
            tag.ToTable("ConstraintFaultTags");
            tag.HasOne<Category>()
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .HasConstraintName(TagCategoryKey)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}

[UsedImplicitly]
public sealed class PostgreSqlConstraintFaultFixture : HeadlessPostgreSqlFixture, IConstraintFaultFixture
{
    public bool ReportsConstraintNames => true;

    public bool EnforcesColumnTypes => true;

    public ConstraintFaultDbContext CreateContext()
    {
        return new(new DbContextOptionsBuilder<ConstraintFaultDbContext>().UseNpgsql(ConnectionString).Options);
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }
}

[UsedImplicitly]
public sealed class SqlServerConstraintFaultFixture : HeadlessSqlServerFixture, IConstraintFaultFixture
{
    public bool ReportsConstraintNames => true;

    public bool EnforcesColumnTypes => true;

    public ConstraintFaultDbContext CreateContext()
    {
        return new(new DbContextOptionsBuilder<ConstraintFaultDbContext>().UseSqlServer(ConnectionString).Options);
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>An in-memory database kept alive by one open connection, which EF Core opens with foreign keys on.</summary>
[UsedImplicitly]
public sealed class SqliteConstraintFaultFixture : IConstraintFaultFixture, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public bool ReportsConstraintNames => false;

    public bool EnforcesColumnTypes => false;

    public ConstraintFaultDbContext CreateContext()
    {
        return new(new DbContextOptionsBuilder<ConstraintFaultDbContext>().UseSqlite(_connection).Options);
    }

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

public sealed class PostgreSqlConstraintFaultTests(PostgreSqlConstraintFaultFixture fixture)
    : ConstraintFaultTests<PostgreSqlConstraintFaultFixture>(fixture),
        IClassFixture<PostgreSqlConstraintFaultFixture>
{
    [Fact]
    public async Task should_recognize_a_number_beyond_its_column_precision()
    {
        var category = await _InsertCategoryAsync();

        await using var db = Fixture.CreateContext();
        db.Products.Add(_NewProduct(category.Id, price: 123_456m));

        var exception = await _SaveFailureAsync(db);

        RelationalFaults.IsNumericOverflow(exception).Should().BeTrue();
        RelationalFaults.IsMaxLengthExceeded(exception).Should().BeFalse();
    }
}

public sealed class SqlServerConstraintFaultTests(SqlServerConstraintFaultFixture fixture)
    : ConstraintFaultTests<SqlServerConstraintFaultFixture>(fixture),
        IClassFixture<SqlServerConstraintFaultFixture>
{
    [Fact]
    public async Task should_count_a_localized_foreign_key_violation_without_its_name()
    {
        var category = await _InsertCategoryAsync();
        await _InsertAsync(new Tag { CategoryId = category.Id });

        await using var db = Fixture.CreateContext();
        await db.Database.OpenConnectionAsync(AbortToken);
        await db.Database.ExecuteSqlRawAsync("SET LANGUAGE Deutsch", AbortToken);
        db.Categories.Remove(await db.Categories.SingleAsync(x => x.Id == category.Id, AbortToken));

        var exception = await _SaveFailureAsync(db);

        exception.InnerException!.Message.Should().NotStartWith("The DELETE statement", "the server localized it");
        RelationalFaults.IsReferenceViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().BeNull();
    }

    [Fact]
    public async Task should_recognize_an_overflow_converting_to_a_decimal()
    {
        await using var db = Fixture.CreateContext();

        var query = () =>
            db.Database.SqlQueryRaw<decimal>("SELECT CAST(123456 AS decimal(5, 2)) AS [Value]").ToListAsync(AbortToken);

        var exception = (await query.Should().ThrowAsync<Exception>()).Which;
        RelationalFaults
            .GetSqlServerErrorNumbers(RelationalFaults.FindDatabaseException(exception)!)
            .Should()
            .Contain(8115);
        RelationalFaults.IsNumericOverflow(exception).Should().BeTrue();
    }

    [Fact]
    public async Task should_not_classify_a_decimal_parameter_that_sql_client_refuses_before_sending()
    {
        // SqlClient checks a decimal parameter against the column's declared precision itself and throws
        // ArgumentException, so no database error exists to classify.
        var category = await _InsertCategoryAsync();

        await using var db = Fixture.CreateContext();
        db.Products.Add(_NewProduct(category.Id, price: 123_456m));

        var exception = await _SaveFailureAsync(db);

        exception.InnerException.Should().BeOfType<ArgumentException>();
        RelationalFaults.IsNumericOverflow(exception).Should().BeFalse();
    }

    [Fact]
    public async Task should_recognize_an_overflow_for_a_data_type()
    {
        await using var db = Fixture.CreateContext();

        var query = () =>
            db.Database.SqlQueryRaw<byte>("SELECT CAST(300 AS tinyint) AS [Value]").ToListAsync(AbortToken);

        var exception = (await query.Should().ThrowAsync<Exception>()).Which;
        RelationalFaults
            .GetSqlServerErrorNumbers(RelationalFaults.FindDatabaseException(exception)!)
            .Should()
            .Contain(220);
        RelationalFaults.IsNumericOverflow(exception).Should().BeTrue();
    }
}

public sealed class SqliteConstraintFaultTests(SqliteConstraintFaultFixture fixture)
    : ConstraintFaultTests<SqliteConstraintFaultFixture>(fixture),
        IClassFixture<SqliteConstraintFaultFixture>;
