// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.
/// <summary>
/// Proves the feature-owned <c>ConfigureStorage(o =&gt; o.Schema = …)</c> setting reaches the SQL Server fencing
/// sequence now that the setting moved off <c>SqlServerDistributedLockOptions</c>.
/// </summary>
[Collection<SqlServerDistributedLockFixture>]
public sealed class SqlServerCustomSchemaTests(SqlServerDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_create_the_fencing_sequence_in_the_configured_schema()
    {
        var schema = $"locks_{Faker.Random.AlphaNumeric(8).ToLowerInvariant()}";

        await using var provider = _BuildProvider(schema);
        var locks = provider.GetRequiredService<IDistributedLock>();

        var handle = await locks.AcquireAsync(
            Faker.Random.AlphaNumeric(12),
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        handle.FencingToken.Should().NotBeNull();
        await handle.ReleaseAsync();

        var sequences = await _ListSequencesAsync(schema);

        sequences.Should().ContainSingle();
    }

    [Fact]
    public async Task should_default_to_the_feature_schema_when_storage_is_not_configured()
    {
        await using var provider = _BuildProvider(schema: null);
        var locks = provider.GetRequiredService<IDistributedLock>();

        var handle = await locks.AcquireAsync(
            Faker.Random.AlphaNumeric(12),
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
            AbortToken
        );

        handle.FencingToken.Should().NotBeNull();
        await handle.ReleaseAsync();

        var sequences = await _ListSequencesAsync(DistributedLocksStorageOptions.DefaultSchema);

        sequences.Should().NotBeEmpty();
    }

    private ServiceProvider _BuildProvider(string? schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
        {
            if (schema is not null)
            {
                setup.ConfigureStorage(storage => storage.Schema = schema);
            }

            setup.UseSqlServer(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = $"custom-schema:{Faker.Random.AlphaNumeric(6)}:";
            });
        });

        return services.BuildServiceProvider();
    }

    private async Task<IReadOnlyList<string>> _ListSequencesAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT q.name
            FROM sys.sequences q
            JOIN sys.schemas s ON s.schema_id = q.schema_id
            WHERE s.name = @schema;
            """;
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
