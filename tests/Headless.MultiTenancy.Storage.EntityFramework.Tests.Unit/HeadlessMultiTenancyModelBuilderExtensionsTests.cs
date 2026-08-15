// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

public sealed class HeadlessMultiTenancyModelBuilderExtensionsTests
{
    [Fact]
    public void should_configure_table_name_and_unique_index_on_normalized_identifier()
    {
        // given & when
        using var db = _CreateSqliteContext();
        var entity = _TenantEntity(db);

        // then
        entity.GetTableName().Should().Be("Tenants");
        var index = entity.GetIndexes().Should().ContainSingle().Which;
        index.IsUnique.Should().BeTrue();
        index.Properties.Select(p => p.Name).Should().Equal(nameof(TenantRecord.NormalizedIdentifier));
    }

    [Fact]
    public void should_configure_column_max_lengths()
    {
        // given & when
        using var db = _CreateSqliteContext();
        var entity = _TenantEntity(db);

        // then
        entity.FindProperty(nameof(TenantRecord.Id))!.GetMaxLength().Should().Be(DomainConstants.IdMaxLength);
        entity
            .FindProperty(nameof(TenantRecord.Identifier))!
            .GetMaxLength()
            .Should()
            .Be(TenantRecordConstants.IdentifierMaxLength);
        entity
            .FindProperty(nameof(TenantRecord.NormalizedIdentifier))!
            .GetMaxLength()
            .Should()
            .Be(TenantRecordConstants.IdentifierMaxLength);
        entity.FindProperty(nameof(TenantRecord.Name))!.GetMaxLength().Should().Be(TenantRecordConstants.NameMaxLength);
    }

    [Fact]
    public void should_require_identifier_and_normalized_identifier()
    {
        // given & when
        using var db = _CreateSqliteContext();
        var entity = _TenantEntity(db);

        // then
        entity.FindProperty(nameof(TenantRecord.Identifier))!.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(TenantRecord.NormalizedIdentifier))!.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void should_configure_extra_properties_with_a_value_converter()
    {
        // given & when
        using var db = _CreateSqliteContext();
        var entity = _TenantEntity(db);

        // then
        var property = entity.FindProperty(nameof(TenantRecord.ExtraProperties));
        property.Should().NotBeNull();
        property!.GetValueConverter().Should().NotBeNull();
    }

    [Fact]
    public void should_apply_no_collation_on_a_provider_without_a_known_mapping()
    {
        // given & when - SQLite has no known collation mapping; KTD6 only defines SQL Server/PostgreSQL
        using var db = _CreateSqliteContext();
        var entity = _TenantEntity(db);

        // then
        entity.FindProperty(nameof(TenantRecord.NormalizedIdentifier))!.GetCollation().Should().BeNull();
    }

    [Fact]
    public void should_apply_deterministic_binary_collation_on_sql_server()
    {
        // given & when - SQL Server's default collation is case-insensitive, which would break the
        // catalog service's ordinal lookup contract (R7); model building needs no live connection.
        using var db = new SqlServerTenantDbContext(
            new DbContextOptionsBuilder<SqlServerTenantDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=tenant_catalog_model_test;Trusted_Connection=True;TrustServerCertificate=True;"
                )
                .Options
        );
        var entity = _TenantEntity(db);

        // then
        entity
            .FindProperty(nameof(TenantRecord.NormalizedIdentifier))!
            .GetCollation()
            .Should()
            .Be("Latin1_General_100_BIN2");
    }

    [Fact]
    public void should_apply_c_collation_on_postgre_sql()
    {
        // given & when
        using var db = new PostgreSqlTenantDbContext(
            new DbContextOptionsBuilder<PostgreSqlTenantDbContext>()
                .UseNpgsql("Host=localhost;Database=tenant_catalog_model_test;Username=test;Password=test;")
                .Options
        );
        var entity = _TenantEntity(db);

        // then
        entity.FindProperty(nameof(TenantRecord.NormalizedIdentifier))!.GetCollation().Should().Be("C");
    }

    private static IEntityType _TenantEntity(DbContext db)
    {
        // The read-optimized runtime model (db.Model) omits collation metadata; the design-time model
        // retains it, matching what tooling such as `dotnet ef` inspects.
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(TenantRecord));
        entity.Should().NotBeNull();
        return entity!;
    }

    private static SqliteTenantDbContext _CreateSqliteContext()
    {
        return new SqliteTenantDbContext(
            new DbContextOptionsBuilder<SqliteTenantDbContext>().UseSqlite("Data Source=:memory:").Options
        );
    }

    private sealed class SqliteTenantDbContext(DbContextOptions<SqliteTenantDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessTenancyCatalog(this);
        }
    }

    private sealed class SqlServerTenantDbContext(DbContextOptions<SqlServerTenantDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessTenancyCatalog(this);
        }
    }

    private sealed class PostgreSqlTenantDbContext(DbContextOptions<PostgreSqlTenantDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessTenancyCatalog(this);
        }
    }
}
