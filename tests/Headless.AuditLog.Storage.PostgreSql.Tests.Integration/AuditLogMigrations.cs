// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

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

    /// <summary>
    /// The same script with the audit table created partitioned by hand, for an application that writes the table's
    /// SQL itself instead of letting <c>AddHeadlessAuditLogPartitions</c> change the scaffolded table creation.
    /// </summary>
    public static string PartitionedCreateScript<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var script = CreateScript(create);
        var end = script.IndexOf(
            "\n);",
            script.IndexOf("CREATE TABLE", StringComparison.Ordinal),
            StringComparison.Ordinal
        );

        return script.Insert(end + 2, " PARTITION BY RANGE (created_at)");
    }

    /// <summary>
    /// The operations <c>dotnet ef migrations add</c> scaffolds into the first migration of
    /// <typeparamref name="TContext"/>: the schema, each table with its keys and constraints, then the indexes.
    /// </summary>
    public static IReadOnlyList<MigrationOperation> ScaffoldedOperations<TContext>(
        Func<DbContextOptions<TContext>, TContext> create
    )
        where TContext : DbContext
    {
        using var context = create(new DbContextOptionsBuilder<TContext>().UseNpgsql().Options);
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        return context.GetService<IMigrationsModelDiffer>().GetDifferences(source: null, model);
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

        migrationBuilder.Sql(AuditLogMigrationScripts.PartitionedCreateScript<PartitionedAuditDbContext>(o => new(o)));
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

/// <summary>
/// Partitioned by the helper alone: the migration keeps the scaffolded table creation, with a column and a check
/// constraint the application added to the audit entity, and grants the partition functions to the application login.
/// </summary>
public sealed class ScaffoldedPartitionedAuditDbContext(DbContextOptions<ScaffoldedPartitionedAuditDbContext> options)
    : DbContext(options)
{
    public const string Schema = "audit_log_ef_scaffolded";
    public const string Category = "Category";
    public const string CategoryCheck = "ck_audit_log_entries_category";
    public const string TableComment = "Who did what, and when.";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AuditLogMigrationScripts.MapAuditLog(modelBuilder, Schema);

        modelBuilder.Entity<AuditLogEntry>(entry =>
        {
            entry.Property<string>(Category).HasColumnName("category").HasMaxLength(32).HasDefaultValue("data");
            entry.ToTable(table =>
            {
                table.HasComment(TableComment);
                table.HasCheckConstraint(CategoryCheck, "category <> ''");
            });
        });
    }
}

[DbContext(typeof(ScaffoldedPartitionedAuditDbContext))]
[Migration("20260101000000_CreateScaffoldedPartitionedAuditLog")]
public sealed class CreateScaffoldedPartitionedAuditLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string schema = ScaffoldedPartitionedAuditDbContext.Schema;

        migrationBuilder.Operations.AddRange(
            AuditLogMigrationScripts.ScaffoldedOperations<ScaffoldedPartitionedAuditDbContext>(o => new(o))
        );
        migrationBuilder.AddHeadlessAuditLogPartitions(schema: schema, monthsAhead: 1);
        migrationBuilder.AddHeadlessAuditLogAppendOnly(schema: schema);
        migrationBuilder.Sql(
            $"""
            GRANT USAGE ON SCHEMA "{schema}" TO "{PostgreSqlAuditLogRolesFixture.App}";
            GRANT SELECT, INSERT ON "{schema}"."audit_log_entries" TO "{PostgreSqlAuditLogRolesFixture.App}";
            """
        );
        migrationBuilder.Sql(
            PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement(
                PostgreSqlAuditLogRolesFixture.App,
                schema: schema
            )
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        const string schema = ScaffoldedPartitionedAuditDbContext.Schema;

        migrationBuilder.DropHeadlessAuditLogAppendOnly(schema: schema);
        migrationBuilder.DropHeadlessAuditLogPartitions(schema: schema);
        migrationBuilder.DropTable("audit_log_entries", schema);
    }
}
