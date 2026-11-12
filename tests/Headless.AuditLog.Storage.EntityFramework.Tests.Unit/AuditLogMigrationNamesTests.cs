// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.AuditLog.Internal;

namespace Tests;

public sealed class AuditLogMigrationNamesTests
{
    [Theory]
    [InlineData(StorageProvider.PostgreSql, "audit_log_entries")]
    [InlineData(StorageProvider.SqlServer, "AuditLogEntries")]
    public void should_resolve_the_conventional_table_name_of_the_database(StorageProvider provider, string expected)
    {
        AuditLogMigrationNames.Resolve(provider, "headless", table: null).Should().Be(expected);
    }

    [Fact]
    public void should_use_a_configured_table_name_verbatim()
    {
        AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, "compliance", "audit").Should().Be("audit");
    }

    [Theory]
    [InlineData("audit\"; DROP TABLE x; --", "headless")]
    [InlineData("audit", "head less")]
    [InlineData("audit", "")]
    public void should_refuse_a_name_that_is_not_a_plain_identifier(string table, string schema)
    {
        // when - the names are spliced into DDL
        var act = () => AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, schema, table);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_refuse_a_name_longer_than_the_database_keeps()
    {
        // given - 64 characters fit SQL Server and not PostgreSQL
        var table = new string('a', 64);

        // when
        var postgreSql = () => AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, "headless", table);
        var sqlServer = () => AuditLogMigrationNames.Resolve(StorageProvider.SqlServer, "headless", table);

        // then
        postgreSql.Should().Throw<ArgumentException>();
        sqlServer.Should().NotThrow();
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData(null)]
    public void should_refuse_a_migration_for_another_database(string? activeProvider)
    {
        // when
        var act = () =>
            AuditLogMigrationNames.EnsureProvider(activeProvider, StorageProvider.PostgreSql, "Some.Package");

        // then
        act.Should().Throw<NotSupportedException>().WithMessage("*Some.Package*PostgreSql*");
    }

    [Fact]
    public void should_accept_a_migration_for_the_expected_database()
    {
        // when
        var act = () =>
            AuditLogMigrationNames.EnsureProvider(
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StorageProvider.PostgreSql,
                "Some.Package"
            );

        // then
        act.Should().NotThrow();
    }
}
