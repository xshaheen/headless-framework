// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Tests;

public sealed class PostgreSqlAuditLogPartitionFunctionsTests
{
    [Fact]
    public void should_grant_both_partition_functions_of_the_conventional_table()
    {
        // when
        var sql = PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement("app");

        // then
        sql.Should()
            .Be(
                """GRANT EXECUTE ON FUNCTION "headless"."audit_log_entries_ensure_partitions", "headless"."audit_log_entries_drop_partitions" TO "app";"""
            );
    }

    [Fact]
    public void should_use_a_configured_schema_and_table_verbatim()
    {
        // when
        var sql = PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement(
            "app",
            table: "audit",
            schema: "compliance"
        );

        // then
        sql.Should()
            .Be(
                """GRANT EXECUTE ON FUNCTION "compliance"."audit_ensure_partitions", "compliance"."audit_drop_partitions" TO "app";"""
            );
    }

    [Fact]
    public void should_quote_a_role_name_that_is_not_a_plain_identifier()
    {
        // when - a role may carry any character, so it is quoted rather than refused
        var sql = PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement("""api-"login""");

        // then
        sql.Should().EndWith("""TO "api-""login";""");
    }

    [Theory]
    [InlineData("app", "audit\"; DROP TABLE x; --", "headless")]
    [InlineData("app", "audit", "head less")]
    [InlineData("app", "audit", "")]
    [InlineData("", "audit", "headless")]
    [InlineData(" ", "audit", "headless")]
    public void should_refuse_a_name_it_cannot_splice_into_the_statement(string role, string table, string schema)
    {
        // when
        var act = () => PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement(role, table, schema);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_refuse_a_schema_or_table_longer_than_a_postgresql_identifier()
    {
        // given - 64 characters, one more than PostgreSQL keeps
        var tooLong = new string('a', 64);

        // when
        var schema = () => PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement("app", schema: tooLong);
        var table = () => PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement("app", table: tooLong);

        // then
        schema.Should().Throw<ArgumentException>().WithParameterName("schema");
        table.Should().Throw<ArgumentException>().WithParameterName("table");
    }

    [Fact]
    public void should_refuse_a_role_name_longer_than_a_postgresql_identifier()
    {
        // when
        var act = () => PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement(new string('r', 64));

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*longer than a PostgreSQL identifier*");
    }
}
