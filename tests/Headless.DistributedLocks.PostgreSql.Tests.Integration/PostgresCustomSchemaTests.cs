// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
/// <summary>
/// Proves the feature-owned <c>ConfigureStorage(o =&gt; o.Schema = …)</c> setting reaches the PostgreSQL fencing
/// sequence. This provider previously had no schema setting at all: it created the sequence unqualified, so it
/// landed wherever <c>search_path</c> pointed.
/// </summary>
[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresCustomSchemaTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_the_fencing_sequence_in_the_configured_schema()
    {
        var schema = $"locks_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup
                .ConfigureStorage(storage => storage.Schema = schema)
                .UsePostgreSql(options =>
                {
                    options.ConnectionString = fixture.ConnectionString;
                    options.KeyPrefix = $"custom-schema:{Faker.Random.AlphaNumeric(6)}:";
                })
        );

        await using var provider = services.BuildServiceProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();

        var handle = await locks.AcquireAsync(
            Faker.Random.AlphaNumeric(12),
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        // A fencing token is only issued once the sequence exists, so this proves the acquire path resolved
        // the qualified sequence rather than silently skipping fencing.
        handle.FencingToken.Should().NotBeNull();
        await handle.ReleaseAsync();

        var sequences = await _ListSequencesAsync(schema);

        sequences.Should().Equal(["headless_distributed_locks_fence"]);
    }

    private async Task<IReadOnlyList<string>> _ListSequencesAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequencename FROM pg_sequences WHERE schemaname = @schema;";
        command.Parameters.AddWithValue("schema", schema);

        var sequences = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(AbortToken);

        while (await reader.ReadAsync(AbortToken))
        {
            sequences.Add(reader.GetString(0));
        }

        return sequences;
    }
}
