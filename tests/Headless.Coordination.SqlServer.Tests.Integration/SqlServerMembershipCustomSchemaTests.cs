// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
/// <summary>
/// Proves the feature-owned <c>ConfigureStorage(o =&gt; o.Schema = …)</c> setting reaches the SQL Server DDL and
/// DML now that the setting moved off <c>SqlServerCoordinationOptions</c>.
/// </summary>
[Collection<SqlServerMembershipFixture>]
public sealed class SqlServerMembershipCustomSchemaTests(SqlServerMembershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_membership_tables_in_the_configured_schema()
    {
        var schema = $"coord_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";
        var cluster = Faker.Random.AlphaNumeric(10);

        // Testcontainers reuses the SQL Server container, so dbo can still hold membership tables written by a
        // build from before the schema moved off the provider options. Clear them so the dbo assertion below
        // measures this run rather than the container's history.
        await _DropDboMembershipTablesAsync();

        await using (var node = await fixture.CreateNodeInSchemaAsync(cluster, "node-a", schema, AbortToken))
        {
            var identity = await node.Membership.RegisterAsync(AbortToken);
            var live = await node.Membership.GetLiveNodesAsync(AbortToken);

            live.Should().Equal([identity]);
        }

        var tables = await _ListTablesAsync(schema);

        tables
            .Should()
            .BeEquivalentTo(["CoordinationNodeGeneration", "CoordinationDescriptor", "CoordinationLiveness"]);

        // dbo must hold none of them: a passing round trip alone would also be satisfied by DDL that ignored
        // the option and fell back to the provider's former dbo default. (dbo is not empty — SQL Server keeps
        // its own system tables there.)
        var dboTables = await _ListTablesAsync("dbo");
        dboTables.Should().NotContain(name => name.StartsWith("Coordination", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_default_to_the_feature_schema_when_storage_is_not_configured()
    {
        var cluster = Faker.Random.AlphaNumeric(10);

        await using (var node = await fixture.CreateNodeAsync(cluster, "node-a", AbortToken))
        {
            await node.Membership.RegisterAsync(AbortToken);
        }

        var tables = await _ListTablesAsync(CoordinationStorageOptions.DefaultSchema);

        tables.Should().Contain("CoordinationLiveness");
    }

    private async Task _DropDboMembershipTablesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS dbo.CoordinationLiveness;
            DROP TABLE IF EXISTS dbo.CoordinationDescriptor;
            DROP TABLE IF EXISTS dbo.CoordinationNodeGeneration;
            """;

        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<IReadOnlyList<string>> _ListTablesAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema;
            """;
        command.Parameters.AddWithValue("schema", schema);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
