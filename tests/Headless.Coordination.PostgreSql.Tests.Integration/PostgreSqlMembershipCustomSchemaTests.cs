// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
/// <summary>
/// Proves the feature-owned <c>ConfigureStorage(o =&gt; o.Schema = …)</c> setting actually reaches the PostgreSQL
/// DDL and DML: before this setting existed the provider emitted unqualified table names and the schema was
/// whatever <c>search_path</c> happened to be.
/// </summary>
[Collection<PostgreSqlMembershipFixture>]
public sealed class PostgreSqlMembershipCustomSchemaTests(PostgreSqlMembershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_membership_tables_in_the_configured_schema()
    {
        var schema = $"coord_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";
        var cluster = Faker.Random.AlphaNumeric(10);

        await using (var node = await fixture.CreateNodeInSchemaAsync(cluster, "node-a", schema, AbortToken))
        {
            var identity = await node.Membership.RegisterAsync(AbortToken);
            var live = await node.Membership.GetLiveNodesAsync(AbortToken);

            live.Should().Equal([identity]);
        }

        var tables = await _ListTablesAsync(schema);

        tables
            .Should()
            .BeEquivalentTo(["coordination_node_generation", "coordination_descriptor", "coordination_liveness"]);

        // search_path's schema must hold none of them: a passing round trip alone would also be satisfied by
        // DDL that ignored the option and emitted unqualified names, as this provider did before the option
        // existed.
        var searchPathTables = await _ListTablesAsync("public");
        searchPathTables.Should().NotContain(name => name.StartsWith("coordination_", StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<string>> _ListTablesAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname = @schema;";
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
