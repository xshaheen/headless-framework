// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public sealed class PostgreSqlAuditLogMigrationBuilderExtensionsTests
{
    private const string _Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

    [Fact]
    public void should_emit_the_postgresql_providers_append_only_step_for_the_conventional_table()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        migrationBuilder.AddHeadlessAuditLogAppendOnly();

        // then
        var sql = _Sql(migrationBuilder);
        sql.Should()
            .Contain(
                """CREATE TRIGGER "audit_log_entries_append_only" BEFORE UPDATE OR DELETE ON "headless"."audit_log_entries" FOR EACH ROW EXECUTE FUNCTION "headless"."audit_log_entries_append_only"();"""
            );
        sql.Should()
            .Contain(
                """CREATE TRIGGER "audit_log_entries_append_only_truncate" BEFORE TRUNCATE ON "headless"."audit_log_entries" FOR EACH STATEMENT"""
            );
        sql.Should().NotContain("SECURITY DEFINER");
    }

    [Fact]
    public void should_use_a_configured_schema_and_table_verbatim()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        migrationBuilder.AddHeadlessAuditLogAppendOnly(table: "audit", schema: "compliance");
        migrationBuilder.DropHeadlessAuditLogAppendOnly(table: "audit", schema: "compliance");

        // then
        var operations = migrationBuilder.Operations.Cast<SqlOperation>().ToList();
        operations[0].Sql.Should().Contain("""ON "compliance"."audit" FOR EACH ROW""");
        operations[1].Sql.Should().Contain("""DROP TRIGGER IF EXISTS "audit_append_only" ON "compliance"."audit";""");
        operations[1].Sql.Should().Contain("""DROP FUNCTION IF EXISTS "compliance"."audit_purge";""");
        operations[1].Sql.Should().Contain("""DROP FUNCTION IF EXISTS "compliance"."audit_append_only"() CASCADE;""");
    }

    [Fact]
    public void should_add_the_owners_purge_function_with_its_minimum_retention_when_the_owner_holds_the_purge()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        migrationBuilder.AddHeadlessAuditLogAppendOnly(
            purgeAuthority: AuditLogPurgeAuthority.Owner,
            minimumRetention: TimeSpan.FromDays(30)
        );

        // then
        var sql = _Sql(migrationBuilder);
        sql.Should().Contain("""CREATE OR REPLACE FUNCTION "headless"."audit_log_entries_purge"(""");
        sql.Should().Contain("SECURITY DEFINER SET search_path = pg_catalog, pg_temp");
        sql.Should().Contain("make_interval(secs => 2592000)");
        sql.Should().Contain("""REVOKE ALL ON FUNCTION "headless"."audit_log_entries_purge" FROM PUBLIC;""");
    }

    [Fact]
    public void should_require_a_partitioned_table_install_the_functions_and_create_the_months_ahead()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        migrationBuilder.AddHeadlessAuditLogPartitions(monthsAhead: 6, minimumRetention: TimeSpan.FromDays(30));

        // then
        var sql = _Sql(migrationBuilder);
        sql.Should().StartWith("DO $headless$").And.Contain("is not partitioned");
        sql.Should()
            .NotContain("CREATE TABLE \"headless\"", "this migration holds no table creation for the helper to change");
        sql.Should().Contain("""CREATE OR REPLACE FUNCTION "headless"."audit_log_entries_ensure_partitions"(""");
        sql.Should().Contain("""CREATE OR REPLACE FUNCTION "headless"."audit_log_entries_drop_partitions"(""");
        sql.Should().Contain("make_interval(secs => 2592000)");
        sql.Should().Contain("""PERFORM "headless"."audit_log_entries_ensure_partitions"(pg_catalog.now(), 6);""");
    }

    [Theory]
    [InlineData("audit\"; DROP TABLE x; --", "headless")]
    [InlineData("audit", "head less")]
    [InlineData("audit", "")]
    public void should_refuse_a_name_that_is_not_a_plain_identifier(string table, string schema)
    {
        // given - the names are spliced into DDL
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        var add = () => migrationBuilder.AddHeadlessAuditLogAppendOnly(table, schema);
        var partitions = () => migrationBuilder.AddHeadlessAuditLogPartitions(table, schema);

        // then
        add.Should().Throw<ArgumentException>();
        partitions.Should().Throw<ArgumentException>();
        migrationBuilder.Operations.Should().BeEmpty();
    }

    [Fact]
    public void should_refuse_a_minimum_retention_no_function_would_enforce_and_months_outside_the_range()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        var retentionWithoutOwner = () =>
            migrationBuilder.AddHeadlessAuditLogAppendOnly(minimumRetention: TimeSpan.FromDays(1));
        var subSecondRetention = () =>
            migrationBuilder.AddHeadlessAuditLogPartitions(minimumRetention: TimeSpan.FromMilliseconds(1));
        var tooManyMonths = () => migrationBuilder.AddHeadlessAuditLogPartitions(monthsAhead: 121);
        var missingBuilder = () => ((MigrationBuilder)null!).AddHeadlessAuditLogAppendOnly();

        // then
        retentionWithoutOwner.Should().Throw<ArgumentException>().WithMessage("*Owner*");
        subSecondRetention.Should().Throw<ArgumentException>();
        tooManyMonths.Should().Throw<ArgumentOutOfRangeException>();
        missingBuilder.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_create_the_table_partitioned_when_the_migration_creates_it()
    {
        // given - what EF Core scaffolds: the table with its key and constraints, then its indexes
        var migrationBuilder = new MigrationBuilder(_Npgsql);
        _ScaffoldAuditTable(migrationBuilder, "audit", "compliance");
        migrationBuilder.CreateIndex("ix_audit_tenant_time", "audit", ["tenant_id", "created_at", "id"], "compliance");

        // when
        migrationBuilder.AddHeadlessAuditLogPartitions(table: "audit", schema: "compliance");

        // then - the scaffolded columns become a template, and the key and constraints move to the partitioned table
        var operations = migrationBuilder.Operations;
        operations
            .Select(operation => operation.GetType())
            .Should()
            .Equal(
                typeof(CreateTableOperation),
                typeof(SqlOperation),
                typeof(AddPrimaryKeyOperation),
                typeof(AddUniqueConstraintOperation),
                typeof(AddForeignKeyOperation),
                typeof(CreateIndexOperation),
                typeof(SqlOperation)
            );

        var template = (CreateTableOperation)operations[0];
        template.Name.Should().Be("audit_template");
        template.Schema.Should().Be("compliance");
        template.Columns.Select(column => column.Name).Should().Equal("id", "created_at", "tenant_id", "parent_id");
        template.PrimaryKey.Should().BeNull();
        template.UniqueConstraints.Should().BeEmpty();
        template.ForeignKeys.Should().BeEmpty();
        template.CheckConstraints.Should().ContainSingle();

        var copy = ((SqlOperation)operations[1]).Sql;
        copy.Should()
            .StartWith(
                """CREATE TABLE "compliance"."audit" (LIKE "compliance"."audit_template" INCLUDING ALL EXCLUDING INDEXES) PARTITION BY RANGE ("created_at");"""
            );
        copy.Should().EndWith("""DROP TABLE "compliance"."audit_template";""");

        ((AddPrimaryKeyOperation)operations[2]).Table.Should().Be("audit");
        ((AddPrimaryKeyOperation)operations[2]).Name.Should().Be("pk_audit");
        ((AddUniqueConstraintOperation)operations[3]).Table.Should().Be("audit");
        ((AddForeignKeyOperation)operations[4]).Table.Should().Be("audit");
        ((CreateIndexOperation)operations[5]).Table.Should().Be("audit");
        ((SqlOperation)operations[6]).Sql.Should().Contain("\"compliance\".\"audit_ensure_partitions\"");
    }

    [Fact]
    public void should_leave_other_tables_alone_when_the_migration_does_not_create_the_audit_table()
    {
        // given - another table of the same migration, and the audit table of another schema
        var migrationBuilder = new MigrationBuilder(_Npgsql);
        _ScaffoldAuditTable(migrationBuilder, "orders", "headless");
        _ScaffoldAuditTable(migrationBuilder, "audit_log_entries", "other");

        // when
        migrationBuilder.AddHeadlessAuditLogPartitions();

        // then - only the functions are added; the database refuses a table that is not partitioned
        migrationBuilder.Operations.Should().HaveCount(3);
        ((CreateTableOperation)migrationBuilder.Operations[0]).Name.Should().Be("orders");
        ((CreateTableOperation)migrationBuilder.Operations[0]).PrimaryKey.Should().NotBeNull();
        ((CreateTableOperation)migrationBuilder.Operations[1]).Name.Should().Be("audit_log_entries");
        ((SqlOperation)migrationBuilder.Operations[2]).Sql.Should().Contain("is not partitioned");
    }

    [Fact]
    public void should_refuse_a_table_name_that_leaves_no_room_for_the_template()
    {
        // given - 63 characters, the longest PostgreSQL identifier
        var table = new string('a', 63);
        var migrationBuilder = new MigrationBuilder(_Npgsql);
        _ScaffoldAuditTable(migrationBuilder, table, "headless");

        // when
        var act = () => migrationBuilder.AddHeadlessAuditLogPartitions(table);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*template*");
        migrationBuilder
            .Operations.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<CreateTableOperation>()
            .Which.Name.Should()
            .Be(table);
    }

    [Fact]
    public void should_drop_the_partition_functions()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        migrationBuilder.DropHeadlessAuditLogPartitions(table: "audit", schema: "compliance");

        // then
        _Sql(migrationBuilder)
            .Should()
            .Be(
                """
                DROP FUNCTION IF EXISTS "compliance"."audit_ensure_partitions";
                DROP FUNCTION IF EXISTS "compliance"."audit_drop_partitions";
                """
            );
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    public void should_refuse_a_migration_for_another_database(string activeProvider)
    {
        // given - the SQL is PostgreSQL's, so it must not reach another database's migration
        var migrationBuilder = new MigrationBuilder(activeProvider);

        // when
        var add = () => migrationBuilder.AddHeadlessAuditLogAppendOnly();
        var drop = () => migrationBuilder.DropHeadlessAuditLogAppendOnly();
        var partitions = () => migrationBuilder.AddHeadlessAuditLogPartitions();
        var dropPartitions = () => migrationBuilder.DropHeadlessAuditLogPartitions();

        // then
        add.Should().Throw<NotSupportedException>().WithMessage($"*PostgreSql*'{activeProvider}'*");
        drop.Should().Throw<NotSupportedException>();
        partitions.Should().Throw<NotSupportedException>();
        dropPartitions.Should().Throw<NotSupportedException>();
        migrationBuilder.Operations.Should().BeEmpty();
    }

    private static void _ScaffoldAuditTable(MigrationBuilder migrationBuilder, string table, string schema)
    {
        migrationBuilder.CreateTable(
            name: table,
            columns: columns => new
            {
                id = columns.Column<long>("bigint", nullable: false),
                created_at = columns.Column<DateTime>("timestamp with time zone", nullable: false),
                tenant_id = columns.Column<string>("character varying(128)", nullable: true),
                parent_id = columns.Column<long>("bigint", nullable: true),
            },
            schema: schema,
            constraints: constraints =>
            {
                constraints.PrimaryKey($"pk_{table}", x => new { x.created_at, x.id });
                constraints.UniqueConstraint($"ak_{table}", x => new { x.created_at, x.tenant_id });
                constraints.CheckConstraint($"ck_{table}_tenant", "tenant_id <> ''");
                constraints.ForeignKey($"fk_{table}_parent", x => x.parent_id, "parents", "id", schema);
            }
        );
    }

    private static string _Sql(MigrationBuilder migrationBuilder)
    {
        return migrationBuilder.Operations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject.Sql;
    }
}
