// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class HeadlessAuditLogModelBuilderExtensionsTests : TestBase
{
    [Fact]
    public void should_configure_and_exclude_audit_log_entry()
    {
        // given & when
        using var db = new StandardAuditModelDbContext(_Options<StandardAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "AuditLogEntries", "headless");
        _AssertAuditPolicy(entity, false);
    }

    [Fact]
    public void should_name_every_object_in_snake_case_for_a_snake_case_database()
    {
        // given & when
        using var db = new SnakeCaseAuditModelDbContext(_Options<SnakeCaseAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        entity.GetTableName().Should().Be("audit_log_entries");
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_audit_log_entries");
        entity
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .Should()
            .BeEquivalentTo(
                "ix_audit_log_entries_tenant_time",
                "ix_audit_log_entries_tenant_action_time",
                "ix_audit_log_entries_tenant_entity_time",
                "ix_audit_log_entries_tenant_actor_time",
                "ix_audit_log_entries_tenant_account_time",
                "ix_audit_log_entries_correlation"
            );

        var table = StoreObjectIdentifier.Table("audit_log_entries", "headless");
        entity.FindProperty(nameof(AuditLogEntry.CreatedAt))!.GetColumnName(table).Should().Be("created_at");
        entity.FindProperty(nameof(AuditLogEntry.TenantId))!.GetColumnName(table).Should().Be("tenant_id");
        entity.FindProperty(nameof(AuditLogEntry.ChangedFields))!.GetColumnName(table).Should().Be("changed_fields");
    }

    [Fact]
    public void should_keep_a_configured_table_name_verbatim_in_snake_case()
    {
        // given & when
        using var db = new ConfiguredSnakeCaseAuditModelDbContext(_Options<ConfiguredSnakeCaseAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        entity.GetTableName().Should().Be("TenantAudit");
        entity.FindPrimaryKey()!.GetName().Should().Be("pk_TenantAudit");
        entity.GetIndexes().Select(index => index.GetDatabaseName()).Should().Contain("ix_TenantAudit_tenant_time");
    }

    [Fact]
    public void should_detect_the_naming_style_from_the_context_provider()
    {
        // given — SQLite is not PostgreSQL, so the DbContext overload picks PascalCase.
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ContextOverloadAuditModelDbContext>()
            .UseSqlite("Data Source=:memory:")
            .UseApplicationServiceProvider(services)
            .Options;

        // when
        using var db = new ContextOverloadAuditModelDbContext(options);

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "AuditLogEntries", "headless");
        entity
            .FindProperty(nameof(AuditLogEntry.CreatedAt))!
            .GetColumnName(StoreObjectIdentifier.Table("AuditLogEntries", "headless"))
            .Should()
            .Be("CreatedAt");
    }

    [Fact]
    public void should_fully_configure_pre_registered_audit_log_entry()
    {
        // given & when
        using var db = new PreRegisteredAuditModelDbContext(_Options<PreRegisteredAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "pre_registered_audit", "headless");
        _AssertAuditPolicy(entity, false);
    }

    [Fact]
    public void should_keep_first_complete_configuration_when_registered_repeatedly()
    {
        // given & when
        using var db = new RepeatedAuditModelDbContext(_Options<RepeatedAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "first_audit", "first_schema");
        _AssertAuditPolicy(entity, false);
    }

    [Fact]
    public void should_expose_later_explicit_audit_override()
    {
        // given & when
        using var db = new ExplicitOverrideAuditModelDbContext(_Options<ExplicitOverrideAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "AuditLogEntries", "headless");
        _AssertAuditPolicy(entity, true);
    }

    private static DbContextOptions<TContext> _Options<TContext>()
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options;
    }

    private static IEntityType _AuditLogEntity(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(AuditLogEntry));
        entity.Should().NotBeNull();
        return entity!;
    }

    private static void _AssertFullyConfigured(IEntityType entity, string tableName, string schema)
    {
        entity.GetTableName().Should().Be(tableName);
        entity.GetSchema().Should().Be(schema);
        entity
            .FindPrimaryKey()!
            .Properties.Select(property => property.Name)
            .Should()
            .Equal(nameof(AuditLogEntry.CreatedAt), nameof(AuditLogEntry.Id));
        entity.FindPrimaryKey()!.GetName().Should().Be($"PK_{tableName}");
        // Index names are unique per schema on PostgreSQL, so they carry the table name to let two audit
        // tables share one schema.
        entity
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .Should()
            .BeEquivalentTo(
                $"IX_{tableName}_TenantTime",
                $"IX_{tableName}_TenantActionTime",
                $"IX_{tableName}_TenantEntityTime",
                $"IX_{tableName}_TenantActorTime",
                $"IX_{tableName}_TenantAccountTime",
                $"IX_{tableName}_Correlation"
            );
    }

    private static void _AssertAuditPolicy(IEntityType entity, bool expected)
    {
        var annotation = entity.FindAnnotation(HeadlessModelAnnotations.AuditLog.EntityIsAudited);
        annotation.Should().NotBeNull();
        annotation!.Value.Should().Be(expected);
    }

    private sealed class StandardAuditModelDbContext(DbContextOptions<StandardAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }

    private sealed class SnakeCaseAuditModelDbContext(DbContextOptions<SnakeCaseAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions(), StorageNamingStyle.SnakeCase);
        }
    }

    private sealed class ConfiguredSnakeCaseAuditModelDbContext(
        DbContextOptions<ConfiguredSnakeCaseAuditModelDbContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(
                new AuditLogStorageOptions { TableName = "TenantAudit" },
                StorageNamingStyle.SnakeCase
            );
        }
    }

    private sealed class ContextOverloadAuditModelDbContext(
        DbContextOptions<ContextOverloadAuditModelDbContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(this);
        }
    }

    private sealed class PreRegisteredAuditModelDbContext(DbContextOptions<PreRegisteredAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditLogEntry>();
            modelBuilder.AddHeadlessAuditLog(
                new AuditLogStorageOptions { TableName = "pre_registered_audit" },
                StorageNamingStyle.PascalCase
            );
        }
    }

    private sealed class RepeatedAuditModelDbContext(DbContextOptions<RepeatedAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(
                new AuditLogStorageOptions { TableName = "first_audit", Schema = "first_schema" },
                StorageNamingStyle.PascalCase
            );
            modelBuilder.AddHeadlessAuditLog(
                new AuditLogStorageOptions { TableName = "second_audit", Schema = "second_schema" },
                StorageNamingStyle.SnakeCase
            );
        }
    }

    private sealed class ExplicitOverrideAuditModelDbContext(
        DbContextOptions<ExplicitOverrideAuditModelDbContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions(), StorageNamingStyle.PascalCase);
            modelBuilder.Entity<AuditLogEntry>().IsAudited();
        }
    }
}
