// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgresDistributedLockFixture>]
public sealed class PostgresFencingConcurrentInitTests(PostgresDistributedLockFixture fixture) : TestBase
{
    private const string _SequenceName = "headless_distributed_locks_fence";

    [Fact]
    public async Task should_initialize_fence_sequence_safely_when_many_first_acquirers_race()
    {
        const int racers = 8;

        // Drop the sequence so every racer's source hits the one-time _EnsureSequenceAsync path and they
        // contend on the cross-replica init guard (advisory xact lock + already-exists SqlState handling).
        await _DropSequenceAsync();

        // Each racer gets its own provider (and therefore its own fencing-token source) so the in-process
        // SemaphoreSlim guard does not serialize them — the contention is genuinely cross-source.
        var providers = Enumerable.Range(0, racers).Select(_ => _CreateProvider()).ToArray();

        try
        {
            var resource = Faker.Random.AlphaNumeric(12);

            var acquires = providers
                .Select(p =>
                    Task.Run(
                        async () =>
                        {
                            var locks = p.GetRequiredService<IDistributedLock>();

                            // Distinct resources so the racers do not block on each other's advisory lock —
                            // the point is to race the sequence init, not the lock itself.
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

            // Every racer must have obtained a token, and all tokens are unique and monotonic (a single
            // shared sequence with no duplicates proves the concurrent init produced exactly one sequence).
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

    private async Task _DropSequenceAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SEQUENCE IF EXISTS {_SequenceName}";
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
