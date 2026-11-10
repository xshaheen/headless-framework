// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public sealed class AuditLogMigrationBuilderExtensionsTests
{
    private const string _Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string _SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

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
    public void should_emit_the_sql_server_providers_append_only_step_for_the_conventional_table()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_SqlServer);

        // when
        migrationBuilder.AddHeadlessAuditLogAppendOnly();

        // then
        _Sql(migrationBuilder)
            .Should()
            .StartWith(
                "EXEC(N'CREATE OR ALTER TRIGGER [headless].[TR_AuditLogEntries_AppendOnly] ON [headless].[AuditLogEntries] AFTER UPDATE, DELETE AS"
            );
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
    public void should_drop_the_sql_server_trigger_in_down()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_SqlServer);

        // when
        migrationBuilder.DropHeadlessAuditLogAppendOnly();

        // then
        _Sql(migrationBuilder).Should().Be("DROP TRIGGER IF EXISTS [headless].[TR_AuditLogEntries_AppendOnly];");
    }

    [Fact]
    public void should_partition_the_table_install_the_functions_and_create_the_months_ahead()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_Npgsql);

        // when
        migrationBuilder.AddHeadlessAuditLogPartitions(monthsAhead: 6, minimumRetention: TimeSpan.FromDays(30));

        // then
        var sql = _Sql(migrationBuilder);
        sql.Should().Contain("""PARTITION BY RANGE ("created_at")""");
        sql.Should().Contain("""CREATE OR REPLACE FUNCTION "headless"."audit_log_entries_ensure_partitions"(""");
        sql.Should().Contain("""CREATE OR REPLACE FUNCTION "headless"."audit_log_entries_drop_partitions"(""");
        sql.Should().Contain("make_interval(secs => 2592000)");
        sql.Should().EndWith("""SELECT "headless"."audit_log_entries_ensure_partitions"(pg_catalog.now(), 6);""");
    }

    [Fact]
    public void should_refuse_what_only_postgresql_supports_on_sql_server()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_SqlServer);

        // when
        var ownerPurge = () =>
            migrationBuilder.AddHeadlessAuditLogAppendOnly(purgeAuthority: AuditLogPurgeAuthority.Owner);
        var partitions = () => migrationBuilder.AddHeadlessAuditLogPartitions();

        // then
        ownerPurge.Should().Throw<NotSupportedException>().WithMessage("*PostgreSQL only*");
        partitions.Should().Throw<NotSupportedException>().WithMessage("*PostgreSQL only*");
        migrationBuilder.Operations.Should().BeEmpty();
    }

    [Fact]
    public void should_refuse_a_database_that_is_neither_postgresql_nor_sql_server()
    {
        // given
        var migrationBuilder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        // when
        var add = () => migrationBuilder.AddHeadlessAuditLogAppendOnly();
        var drop = () => migrationBuilder.DropHeadlessAuditLogAppendOnly();
        var partitions = () => migrationBuilder.AddHeadlessAuditLogPartitions();

        // then
        add.Should().Throw<NotSupportedException>().WithMessage("*Sqlite*");
        drop.Should().Throw<NotSupportedException>();
        partitions.Should().Throw<NotSupportedException>();
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

    private static string _Sql(MigrationBuilder migrationBuilder)
    {
        return migrationBuilder.Operations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject.Sql;
    }
}
