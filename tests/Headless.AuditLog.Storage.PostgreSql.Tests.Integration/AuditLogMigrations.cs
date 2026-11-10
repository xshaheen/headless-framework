// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Tests;

// Each context below is an application's DbContext with one hand-written migration, as an application would write
// it: the scaffolded table creation, then the audit log helpers. The table DDL comes from EF's own script for the
// model, so the helpers run against exactly what a scaffolded CreateTable and CreateIndex produce.

internal static class AuditLogMigrationScripts
{
    public const string HistoryTable = "__ef_migrations";

    public static string CreateScript<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        using var context = create(new DbContextOptionsBuilder<TContext>().UseNpgsql().Options);

        return context.Database.GenerateCreateScript();
    }

    public static void MapAuditLog(ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.ConfigureHeadlessAuditLog(
            new AuditLogStorageOptions { Schema = schema },
            StorageNamingStyle.SnakeCase
        );
    }
}

/// <summary>Append-only through a migration, with the default purge authority.</summary>
public sealed class AppendOnlyAuditDbContext(DbContextOptions<AppendOnlyAuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit_log_ef_append_only";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AuditLogMigrationScripts.MapAuditLog(modelBuilder, Schema);
    }
}

[DbContext(typeof(AppendOnlyAuditDbContext))]
[Migration("20260101000000_CreateAppendOnlyAuditLog")]
public sealed class CreateAppendOnlyAuditLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(AuditLogMigrationScripts.CreateScript<AppendOnlyAuditDbContext>(o => new(o)));
        migrationBuilder.AddHeadlessAuditLogAppendOnly(schema: AppendOnlyAuditDbContext.Schema);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropHeadlessAuditLogAppendOnly(schema: AppendOnlyAuditDbContext.Schema);
        migrationBuilder.DropTable("audit_log_entries", AppendOnlyAuditDbContext.Schema);
    }
}

/// <summary>Append-only with the purge held by the table's owner, and the application login granted the function.</summary>
public sealed class OwnerPurgeAuditDbContext(DbContextOptions<OwnerPurgeAuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit_log_ef_owner_purge";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AuditLogMigrationScripts.MapAuditLog(modelBuilder, Schema);
    }
}

[DbContext(typeof(OwnerPurgeAuditDbContext))]
[Migration("20260101000000_CreateOwnerPurgeAuditLog")]
public sealed class CreateOwnerPurgeAuditLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string schema = OwnerPurgeAuditDbContext.Schema;

        migrationBuilder.Sql(AuditLogMigrationScripts.CreateScript<OwnerPurgeAuditDbContext>(o => new(o)));
        migrationBuilder.AddHeadlessAuditLogAppendOnly(
            schema: schema,
            purgeAuthority: AuditLogPurgeAuthority.Owner,
            minimumRetention: TimeSpan.FromDays(30)
        );
        migrationBuilder.Sql(
            $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{PostgreSqlAuditLogRolesFixture.App}";
            GRANT SELECT, INSERT, DELETE ON "{schema}"."audit_log_entries" TO "{PostgreSqlAuditLogRolesFixture.App}";
            GRANT EXECUTE ON FUNCTION "{schema}"."audit_log_entries_purge" TO "{PostgreSqlAuditLogRolesFixture.App}";
            """
        );
    }
}

/// <summary>Partitioned by month and append-only, with the application login granted the partition functions.</summary>
public sealed class PartitionedAuditDbContext(DbContextOptions<PartitionedAuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit_log_ef_partitions";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AuditLogMigrationScripts.MapAuditLog(modelBuilder, Schema);
    }
}

[DbContext(typeof(PartitionedAuditDbContext))]
[Migration("20260101000000_CreatePartitionedAuditLog")]
public sealed class CreatePartitionedAuditLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string schema = PartitionedAuditDbContext.Schema;

        migrationBuilder.Sql(AuditLogMigrationScripts.CreateScript<PartitionedAuditDbContext>(o => new(o)));
        migrationBuilder.AddHeadlessAuditLogPartitions(schema: schema, monthsAhead: 1);
        migrationBuilder.AddHeadlessAuditLogAppendOnly(schema: schema);
        migrationBuilder.Sql(
            $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{PostgreSqlAuditLogRolesFixture.App}";
            GRANT SELECT, INSERT, DELETE ON "{schema}"."audit_log_entries" TO "{PostgreSqlAuditLogRolesFixture.App}";
            GRANT EXECUTE ON FUNCTION "{schema}"."audit_log_entries_ensure_partitions" TO "{PostgreSqlAuditLogRolesFixture.App}";
            GRANT EXECUTE ON FUNCTION "{schema}"."audit_log_entries_drop_partitions" TO "{PostgreSqlAuditLogRolesFixture.App}";
            """
        );
    }
}
