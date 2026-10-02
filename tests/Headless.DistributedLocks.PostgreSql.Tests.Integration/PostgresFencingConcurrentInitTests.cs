// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgresFencingConcurrentInitTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_initialize_fence_sequence_safely_when_many_first_startups_race()
    {
        const int racers = 8;

        // Drop the schema, and with it the sequence and the runner's history, so every racer's runner finds the
        // step missing and they contend on the runner's database lock and the concurrent-DDL rerun.
        await _DropSchemaAsync();

        // Each racer gets its own provider, and therefore its own runner and fencing-token source, so the contention
        // is genuinely cross-process in shape.
        var providers = Enumerable.Range(0, racers).Select(_ => _CreateProvider()).ToArray();

        try
        {
            await Task.WhenAll(providers.Select(p => p.GetRequiredService<SchemaRunner>().ApplyAsync(AbortToken)));

            var resource = Faker.Random.AlphaNumeric(12);

            var acquires = providers
                .Select(p =>
                    Task.Run(
                        async () =>
                        {
                            var locks = p.GetRequiredService<IDistributedLock>();

                            // Distinct resources so the racers do not block on each other's advisory lock.
                            var handle = await locks.AcquireAsync(
                                $"{resource}:{Guid.NewGuid():N}",
                                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(30) },
                                AbortToken
                            );

                            var token = handle.FencingToken;
                            await handle.ReleaseAsync();

                            return token;
                        },
                        AbortToken
                    )
                )
                .ToArray();

            var tokens = await Task.WhenAll(acquires);

            tokens.Should().AllSatisfy(t => t.Should().NotBeNull());

            var values = tokens.Select(t => t!.Value).ToList();

            // Every racer must have obtained a token, and all tokens are unique (a single shared sequence with no
            // duplicates proves the concurrent startups produced exactly one sequence).
            values.Should().HaveCount(racers);
            values.Should().OnlyHaveUniqueItems();
        }
        finally
        {
            foreach (var provider in providers)
            {
                await provider.DisposeAsync();
            }
        }
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DROP SCHEMA IF EXISTS "{HeadlessStorageDefaults.Schema}" CASCADE""";
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = $"fence-init:{Faker.Random.AlphaNumeric(6)}:";
            })
        );

        return services.BuildServiceProvider();
    }
}
