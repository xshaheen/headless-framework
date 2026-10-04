// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ModelBuildingConventionsTests : TestBase
{
    [Fact]
    public void should_apply_money_and_phone_complex_property_storage_contracts()
    {
        using var context = new ComplexValueDbContext(
            new DbContextOptionsBuilder<ComplexValueDbContext>().UseSqlite("Data Source=:memory:").Options
        );

        var entityType = context.Model.FindEntityType(typeof(ComplexValueRow));
        var price = entityType!.FindComplexProperty(nameof(ComplexValueRow.Price));
        var alternatePrice = entityType.FindComplexProperty(nameof(ComplexValueRow.AlternatePrice));
        var phone = entityType.FindComplexProperty(nameof(ComplexValueRow.Phone));
        var alternatePhone = entityType.FindComplexProperty(nameof(ComplexValueRow.AlternatePhone));

        price.Should().NotBeNull();
        price!.ComplexType.FindProperty(nameof(Money.Amount))!.GetPrecision().Should().Be(32);
        price.ComplexType.FindProperty(nameof(Money.Amount))!.GetScale().Should().Be(10);
        price.ComplexType.FindProperty(nameof(Money.Amount))!.GetColumnName().Should().Be("PriceAmount");
        price.ComplexType.FindProperty(nameof(Money.CurrencyCode))!.GetColumnName().Should().Be("PriceCurrency");
        price.ComplexType.FindProperty(nameof(Money.CurrencyCode))!.GetMaxLength().Should().Be(4);
        alternatePrice.Should().NotBeNull();
        phone.Should().NotBeNull();
        phone!.ComplexType.FindProperty(nameof(PhoneNumber.CountryCode))!.GetColumnName().Should().Be("DialCode");
        phone.ComplexType.FindProperty(nameof(PhoneNumber.Number))!.GetColumnName().Should().Be("Subscriber");
        phone
            .ComplexType.FindProperty(nameof(PhoneNumber.Number))!
            .GetMaxLength()
            .Should()
            .Be(PhoneNumberConstants.Numbers.MaxLength);
        alternatePrice.Should().NotBeNull();
        alternatePrice!.ComplexType.FindProperty(nameof(Money.Amount))!.GetPrecision().Should().Be(32);
        alternatePrice.ComplexType.FindProperty(nameof(Money.Amount))!.GetScale().Should().Be(10);
        alternatePrice.ComplexType.FindProperty(nameof(Money.Amount))!.GetColumnName().Should().Be("Amount");
        alternatePrice.ComplexType.FindProperty(nameof(Money.CurrencyCode))!.GetColumnName().Should().Be("Currency");
        alternatePrice.ComplexType.FindProperty(nameof(Money.CurrencyCode))!.GetMaxLength().Should().Be(3);
        alternatePhone.Should().NotBeNull();
        alternatePhone!
            .ComplexType.FindProperty(nameof(PhoneNumber.CountryCode))!
            .GetColumnName()
            .Should()
            .Be("PhoneCountryCode");
        alternatePhone
            .ComplexType.FindProperty(nameof(PhoneNumber.CountryCode))!
            .GetMaxLength()
            .Should()
            .Be(PhoneNumberConstants.Codes.MaxLength);
        alternatePhone.ComplexType.FindProperty(nameof(PhoneNumber.Number))!.GetColumnName().Should().Be("PhoneNumber");
        alternatePhone
            .ComplexType.FindProperty(nameof(PhoneNumber.Number))!
            .GetMaxLength()
            .Should()
            .Be(PhoneNumberConstants.Numbers.MaxLength);
    }

    [Fact]
    public async Task should_apply_query_filters_for_generic_and_untyped_builders()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        await using var context = new FilterDbContext(
            new DbContextOptionsBuilder<FilterDbContext>().UseSqlite(connection).Options
        );
        await context.Database.EnsureCreatedAsync(AbortToken);
        object[] rows =
        [
            new GenericFilterRow { Id = 1, IsVisible = true },
            new GenericFilterRow { Id = 2, IsVisible = false },
            new GenericFilterRow { Id = 3, IsVisible = true },
            new UntypedFilterRow { Id = 1, IsActive = true },
            new UntypedFilterRow { Id = 2, IsActive = true },
            new UntypedFilterRow { Id = 3, IsActive = false },
        ];
        await context.AddRangeAsync(rows, AbortToken);
        await context.SaveChangesAsync(AbortToken);

        var genericIds = await context.GenericRows.Select(static row => row.Id).ToListAsync(AbortToken);
        var untypedIds = await context.UntypedRows.Select(static row => row.Id).ToListAsync(AbortToken);

        genericIds.Should().BeEquivalentTo([1, 3]);
        untypedIds.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void should_apply_only_matching_entity_configurations_from_an_assembly()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var context = new AssemblyConfigurationDbContext(
            services,
            new DbContextOptionsBuilder<AssemblyConfigurationDbContext>().UseSqlite("Data Source=:memory:").Options
        );

        var property = context.Model.FindEntityType(typeof(AssemblyConfiguredRow))!.FindProperty("Name");

        property!.GetMaxLength().Should().Be(37);
        context.Model.FindEntityType(typeof(ExcludedAssemblyRow)).Should().BeNull();
    }

    [Fact]
    public void should_apply_building_block_converters_and_relational_facets_by_convention()
    {
        using var context = new PrimitiveConventionDbContext(
            new DbContextOptionsBuilder<PrimitiveConventionDbContext>().UseSqlite("Data Source=:memory:").Options
        );
        var entityType = context.Model.FindEntityType(typeof(PrimitiveConventionRow));

        var amount = entityType!.FindProperty(nameof(PrimitiveConventionRow.Amount));
        var accountId = entityType.FindProperty(nameof(PrimitiveConventionRow.AccountId));
        var state = entityType.FindProperty(nameof(PrimitiveConventionRow.State));

        amount!.GetPrecision().Should().Be(32);
        amount.GetScale().Should().Be(10);
        amount.GetTypeMapping().Converter.Should().BeOfType<MoneyAmountValueConverter>();
        accountId!.GetTypeMapping().Converter.Should().BeOfType<AccountIdValueConverter>();
        state!.GetMaxLength().Should().Be(Headless.Domain.DomainConstants.EnumMaxLength);
        state.GetTypeMapping().Converter!.ProviderClrType.Should().Be<string>();
    }

    [Fact]
    public async Task should_save_in_place_mutations_of_json_backed_building_block_values()
    {
        // given - a persisted row whose JSON-backed values are still tracked
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        await using var context = new PrimitiveConventionDbContext(
            new DbContextOptionsBuilder<PrimitiveConventionDbContext>().UseSqlite(connection).Options
        );
        await context.Database.EnsureCreatedAsync(AbortToken);
        var row = new PrimitiveConventionRow
        {
            Id = 1,
            Amount = MoneyAmount.Zero,
            AccountId = new AccountId("account-1"),
            Attachment = _CreateFile(),
            Names = new() { ["en"] = new(StringComparer.Ordinal) { ["name"] = "Book" } },
        };
        context.Rows.Add(row);
        await context.SaveChangesAsync(AbortToken);

        // when - nested mutable members are changed in place rather than replaced
        row.Attachment!.Metadata!["checksum"] = "abc";
        row.Names!["en"]["name"] = "Novel";
        context.ChangeTracker.DetectChanges();
        var entry = context.Entry(row);

        // then - both columns are detected as modified and reach the database
        entry.Property(static x => x.Attachment).IsModified.Should().BeTrue();
        entry.Property(static x => x.Names).IsModified.Should().BeTrue();

        await context.SaveChangesAsync(AbortToken);
        context.ChangeTracker.Clear();
        var stored = await context.Rows.SingleAsync(AbortToken);

        stored.Attachment!.Metadata.Should().ContainKey("checksum");
        stored.Names!["en"]["name"].Should().Be("Novel");
    }

    [Fact]
    public async Task should_leave_unchanged_json_backed_building_block_values_unmodified()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        await using var context = new PrimitiveConventionDbContext(
            new DbContextOptionsBuilder<PrimitiveConventionDbContext>().UseSqlite(connection).Options
        );
        await context.Database.EnsureCreatedAsync(AbortToken);
        var row = new PrimitiveConventionRow
        {
            Id = 1,
            Amount = MoneyAmount.Zero,
            AccountId = new AccountId("account-1"),
            Attachment = _CreateFile(),
            Names = new() { ["en"] = new(StringComparer.Ordinal) { ["name"] = "Book" } },
        };
        context.Rows.Add(row);
        await context.SaveChangesAsync(AbortToken);

        context.ChangeTracker.DetectChanges();

        context.Entry(row).State.Should().Be(EntityState.Unchanged);
    }

    private static Headless.Primitives.File _CreateFile()
    {
        return new Headless.Primitives.File
        {
            Id = "file-1",
            DisplayName = "report.pdf",
            SavedName = "file-1.pdf",
            Url = "https://example.test/file-1.pdf",
            Container = ["reports"],
            ContentType = "application/pdf",
            UploadedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
            Length = 42,
            Private = false,
            Metadata = new(StringComparer.Ordinal) { ["pages"] = 3 },
        };
    }

    private sealed class ComplexValueDbContext(DbContextOptions<ComplexValueDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<ComplexValueRow>();
            entity.HasKey(static row => row.Id);
            entity.HasComplexMoney(
                static row => row.Price,
                amountColumnName: "PriceAmount",
                codeColumnName: "PriceCurrency",
                codeMaxLength: 4
            );
            entity.HasComplexMoney(nameof(ComplexValueRow.AlternatePrice));
            entity.HasComplexPhoneNumber(
                static row => row.Phone,
                codeColumnName: "DialCode",
                phoneColumnName: "Subscriber"
            );
            entity.HasComplexPhoneNumber(nameof(ComplexValueRow.AlternatePhone));
        }
    }

    private sealed class ComplexValueRow
    {
        public int Id { get; init; }

        public required Money Price { get; init; }

        public required Money AlternatePrice { get; init; }

        public required PhoneNumber Phone { get; init; }

        public required PhoneNumber AlternatePhone { get; init; }
    }

    private sealed class FilterDbContext(DbContextOptions<FilterDbContext> options) : DbContext(options)
    {
        public DbSet<GenericFilterRow> GenericRows => Set<GenericFilterRow>();

        public DbSet<UntypedFilterRow> UntypedRows => Set<UntypedFilterRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var generic = modelBuilder.Entity<GenericFilterRow>();
            generic.AndHasQueryFilter(static row => row.IsVisible);

            var untyped = modelBuilder.Entity<UntypedFilterRow>();
            ((EntityTypeBuilder)untyped).AndHasQueryFilter<UntypedFilterRow>(static row => row.IsActive);
        }
    }

    private sealed class GenericFilterRow
    {
        public int Id { get; init; }

        public bool IsVisible { get; init; }
    }

    private sealed class UntypedFilterRow
    {
        public int Id { get; init; }

        public bool IsActive { get; init; }
    }

    private sealed class AssemblyConfigurationDbContext(
        IServiceProvider serviceProvider,
        DbContextOptions<AssemblyConfigurationDbContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(ModelBuildingConventionsTests).Assembly,
                serviceProvider,
                static type => type == typeof(AssemblyConfiguredRowConfiguration)
            );
        }
    }

    public sealed class AssemblyConfiguredRowConfiguration : IEntityTypeConfiguration<AssemblyConfiguredRow>
    {
        public void Configure(EntityTypeBuilder<AssemblyConfiguredRow> builder)
        {
            builder.Property(static row => row.Name).HasMaxLength(37);
        }
    }

    public sealed class AssemblyConfiguredRow
    {
        public int Id { get; init; }

        public required string Name { get; init; }
    }

    public sealed class ExcludedAssemblyRowConfiguration : IEntityTypeConfiguration<ExcludedAssemblyRow>
    {
        public void Configure(EntityTypeBuilder<ExcludedAssemblyRow> builder)
        {
            builder.Property(static row => row.Name).HasMaxLength(99);
        }
    }

    public sealed class ExcludedAssemblyRow
    {
        public int Id { get; init; }

        public required string Name { get; init; }
    }

    private sealed class PrimitiveConventionDbContext(DbContextOptions<PrimitiveConventionDbContext> options)
        : DbContext(options)
    {
        public DbSet<PrimitiveConventionRow> Rows => Set<PrimitiveConventionRow>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddBuildingBlocksPrimitivesConvertersMappings();
        }
    }

    private sealed class PrimitiveConventionRow
    {
        public int Id { get; init; }

        public MoneyAmount Amount { get; init; }

        public required AccountId AccountId { get; init; }

        public PrimitiveState State { get; init; }

        public Headless.Primitives.File? Attachment { get; init; }

        public Locales? Names { get; init; }
    }

    private enum PrimitiveState
    {
        Unknown = 0,
        Active = 1,
    }
}
