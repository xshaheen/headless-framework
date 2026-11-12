// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tests;

public sealed class SqlServerAuditLogMigrationBuilderExtensionsTests
{
    private const string _SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

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
    public void should_use_a_configured_schema_and_table_verbatim()
    {
        // given
        var migrationBuilder = new MigrationBuilder(_SqlServer);

        // when
        migrationBuilder.AddHeadlessAuditLogAppendOnly(table: "Audit", schema: "compliance");
        migrationBuilder.DropHeadlessAuditLogAppendOnly(table: "Audit", schema: "compliance");

        // then
        var operations = migrationBuilder.Operations.Cast<SqlOperation>().ToList();
        operations[0].Sql.Should().Contain("[compliance].[TR_Audit_AppendOnly] ON [compliance].[Audit]");
        operations[1].Sql.Should().Be("DROP TRIGGER IF EXISTS [compliance].[TR_Audit_AppendOnly];");
    }

    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    public void should_refuse_a_migration_for_another_database(string activeProvider)
    {
        // given - the trigger is T-SQL, so it must not reach another database's migration
        var migrationBuilder = new MigrationBuilder(activeProvider);

        // when
        var add = () => migrationBuilder.AddHeadlessAuditLogAppendOnly();
        var drop = () => migrationBuilder.DropHeadlessAuditLogAppendOnly();

        // then
        add.Should().Throw<NotSupportedException>().WithMessage($"*SqlServer*'{activeProvider}'*");
        drop.Should().Throw<NotSupportedException>();
        migrationBuilder.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Audit]; DROP TABLE x; --", "headless")]
    [InlineData("Audit", "head less")]
    [InlineData("Audit", "")]
    public void should_refuse_a_name_that_is_not_a_plain_identifier(string table, string schema)
    {
        // given - the names are spliced into DDL
        var migrationBuilder = new MigrationBuilder(_SqlServer);

        // when
        var add = () => migrationBuilder.AddHeadlessAuditLogAppendOnly(table, schema);
        var drop = () => migrationBuilder.DropHeadlessAuditLogAppendOnly(table, schema);
        var missingBuilder = () => ((MigrationBuilder)null!).AddHeadlessAuditLogAppendOnly();

        // then
        add.Should().Throw<ArgumentException>();
        drop.Should().Throw<ArgumentException>();
        missingBuilder.Should().Throw<ArgumentNullException>();
        migrationBuilder.Operations.Should().BeEmpty();
    }

    private static string _Sql(MigrationBuilder migrationBuilder)
    {
        return migrationBuilder.Operations.Should().ContainSingle().Which.Should().BeOfType<SqlOperation>().Subject.Sql;
    }
}
