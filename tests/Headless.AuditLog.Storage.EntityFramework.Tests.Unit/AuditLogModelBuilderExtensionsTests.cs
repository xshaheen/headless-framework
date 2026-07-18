// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests;

public sealed class AuditLogModelBuilderExtensionsTests : TestBase
{
    [Fact]
    public void should_configure_and_exclude_audit_log_entry()
    {
        // given & when
        using var db = new StandardAuditModelDbContext(_Options<StandardAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "audit_log", "audit");
        _AssertAuditPolicy(entity, false);
    }

    [Fact]
    public void should_fully_configure_pre_registered_audit_log_entry()
    {
        // given & when
        using var db = new PreRegisteredAuditModelDbContext(_Options<PreRegisteredAuditModelDbContext>());

        // then
        var entity = _AuditLogEntity(db);
        _AssertFullyConfigured(entity, "pre_registered_audit", "audit");
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
        _AssertFullyConfigured(entity, "audit_log", "audit");
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
        entity.GetIndexes().Should().HaveCount(5);
    }

    private static void _AssertAuditPolicy(IEntityType entity, bool expected)
    {
        var annotation = entity.FindAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited);
        annotation.Should().NotBeNull();
        annotation!.Value.Should().Be(expected);
    }

    private sealed class StandardAuditModelDbContext(DbContextOptions<StandardAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions());
        }
    }

    private sealed class PreRegisteredAuditModelDbContext(DbContextOptions<PreRegisteredAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditLogEntry>();
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions { TableName = "pre_registered_audit" });
        }
    }

    private sealed class RepeatedAuditModelDbContext(DbContextOptions<RepeatedAuditModelDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(
                new AuditLogStorageOptions { TableName = "first_audit", Schema = "first_schema" }
            );
            modelBuilder.AddHeadlessAuditLog(
                new AuditLogStorageOptions { TableName = "second_audit", Schema = "second_schema" }
            );
        }
    }

    private sealed class ExplicitOverrideAuditModelDbContext(
        DbContextOptions<ExplicitOverrideAuditModelDbContext> options
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions());
            modelBuilder.Entity<AuditLogEntry>().IsAudited();
        }
    }
}
