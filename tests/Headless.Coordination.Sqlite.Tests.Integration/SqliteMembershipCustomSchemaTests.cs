// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Proves the feature-owned <c>ConfigureStorage(o =&gt; o.Schema = …)</c> setting reaches the SQLite DDL and DML, where
/// a schema is a prefix of every object name.
/// </summary>
[Collection<SqliteMembershipFixture>]
public sealed class SqliteMembershipCustomSchemaTests(SqliteMembershipFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_membership_tables_with_the_configured_schema_prefix()
    {
        var schema = $"coord_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";
        var cluster = Faker.Random.AlphaNumeric(10);

        await using (var node = await fixture.CreateNodeInSchemaAsync(cluster, "node-a", schema, AbortToken))
        {
            var identity = await node.Membership.RegisterAsync(AbortToken);
            var live = await node.Membership.GetLiveNodesAsync(AbortToken);

            live.Should().Equal([identity]);
        }

        var tables = await fixture.ListTablesAsync(AbortToken);

        tables
            .Where(name => name.StartsWith(schema + "_", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo([
                $"{schema}_coordination_node_generation",
                $"{schema}_coordination_descriptor",
                $"{schema}_coordination_liveness",
                // The schema runner records applied steps next to the feature's tables.
                $"{schema}_headless_schema_history",
            ]);
        tables.Should().NotContain(name => name.StartsWith("coordination_", StringComparison.Ordinal));
    }
}
