// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Hosting.Initialization;
using Headless.Sequences;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

/// <summary>
/// Proves an initializer still creates its objects when a creator outside the Headless schema-wide lock, such as a
/// consumer's EF migration, commits <c>CREATE SCHEMA</c> first.
/// </summary>
/// <remarks>
/// The foreign transaction creates the schema and stays open, so the initializer's own
/// <c>CREATE SCHEMA IF NOT EXISTS</c> misses the uncommitted row and blocks on the <c>pg_namespace</c> unique index.
/// Committing the foreign transaction then fails the initializer's statement with <c>23505</c> every time, which
/// makes the race deterministic.
/// </remarks>
[Collection<PostgreSqlSharedSchemaFixture>]
public sealed class PostgreSqlForeignSchemaCreatorTests(PostgreSqlSharedSchemaFixture fixture) : TestBase
{
    private static readonly TimeSpan _BlockTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_create_its_table_when_a_foreign_transaction_commits_the_schema_first()
    {
        // given
        var connectionString = await _CreateDatabaseAsync();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPostgreSqlSql(connectionString);
            services.AddHeadlessSequences(setup => setup.UsePostgreSql());

            await using var provider = services.BuildServiceProvider();
            var initializer = provider.GetServices<IHostedService>().OfType<HostedInitializer>().Single();

            // when
            await _RaceForeignSchemaCreatorAsync(connectionString, () => initializer.InitializeAsync(AbortToken));

            // then
            (await _RelationExistsAsync(connectionString, $"\"{HeadlessStorageDefaults.Schema}\".\"sequences\""))
                .Should()
                .BeTrue("the rollback of the raced DDL must be followed by a rerun that creates the table");
        }
        finally
        {
            await _DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task should_issue_a_fencing_token_when_a_foreign_transaction_commits_the_schema_first()
    {
        // given
        var connectionString = await _CreateDatabaseAsync();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPostgreSqlSql(connectionString);
            services.AddHeadlessDistributedLocks(setup => setup.UsePostgreSql());

            await using var provider = services.BuildServiceProvider();
            var locks = provider.GetRequiredService<IDistributedLock>();
            var resource = Faker.Random.AlphaNumeric(12);

            // when: the first acquire creates the fencing sequence lazily, racing the foreign schema creator
            await _RaceForeignSchemaCreatorAsync(
                connectionString,
                async () =>
                {
                    var handle = await locks.AcquireAsync(
                        resource,
                        new DistributedLockAcquireOptions { AcquireTimeout = _BlockTimeout },
                        AbortToken
                    );

                    // A token is issued only by a nextval on the sequence, so this proves the sequence exists.
                    handle.FencingToken.Should().NotBeNull();
                    await handle.ReleaseAsync();
                }
            );

            // then
            (
                await _RelationExistsAsync(
                    connectionString,
                    $"\"{HeadlessStorageDefaults.Schema}\".\"headless_distributed_locks_fence\""
                )
            )
                .Should()
                .BeTrue();
        }
        finally
        {
            await _DropDatabaseAsync(connectionString);
        }
    }

    private async Task _RaceForeignSchemaCreatorAsync(string connectionString, Func<Task> initialize)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;

        await using var foreign = new NpgsqlConnection(connectionString);
        await foreign.OpenAsync(AbortToken);
        await using var transaction = await foreign.BeginTransactionAsync(AbortToken);

        await using (
            var create = new NpgsqlCommand(
                $"""CREATE SCHEMA "{HeadlessStorageDefaults.Schema}";""",
                foreign,
                transaction
            )
        )
        {
            await create.ExecuteNonQueryAsync(AbortToken);
        }

        var initialization = Task.Run(initialize, AbortToken);

        await _WaitUntilASessionBlocksOnALockAsync(connectionString, database, initialization);
        await transaction.CommitAsync(AbortToken);

        await initialization.WaitAsync(_BlockTimeout, AbortToken);
    }

    // Commit only once the initializer is parked on the uncommitted schema row. Committing earlier lets its
    // IF NOT EXISTS see the schema, and the race this test exists for never happens. The probe runs on its own
    // autocommit connection: pg_stat_activity is snapshotted once per transaction, so polling it inside the foreign
    // transaction would never observe the wait.
    private async Task _WaitUntilASessionBlocksOnALockAsync(
        string connectionString,
        string database,
        Task initialization
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_BlockTimeout);

        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(timeout.Token);

        while (true)
        {
            if (initialization.IsCompleted)
            {
                // Surface the initializer's own failure rather than a timeout.
                await initialization;

                throw new InvalidOperationException("The initializer finished without blocking on the foreign schema.");
            }

            await using var probe = new NpgsqlCommand(
                """
                SELECT count(*)::int FROM pg_stat_activity
                WHERE datname = @database AND wait_event_type = 'Lock';
                """,
                observer
            );
            probe.Parameters.AddWithValue("database", database);

            if ((int)(await probe.ExecuteScalarAsync(timeout.Token))! > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
        }
    }

    private async Task<bool> _RelationExistsAsync(string connectionString, string qualifiedName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL;", connection);
        command.Parameters.AddWithValue("name", qualifiedName);

        return (bool)(await command.ExecuteScalarAsync(AbortToken))!;
    }

    private async Task<string> _CreateDatabaseAsync()
    {
        var database = $"foreign_schema_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(AbortToken);
            await using var command = new NpgsqlCommand($"""CREATE DATABASE "{database}";""", connection);
            await command.ExecuteNonQueryAsync(AbortToken);
        }

        return new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ToString();
    }

    private async Task _DropDatabaseAsync(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{database}" WITH (FORCE);""",
            connection
        );
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
