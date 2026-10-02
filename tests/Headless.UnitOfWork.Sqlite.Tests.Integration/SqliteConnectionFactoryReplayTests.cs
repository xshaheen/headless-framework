// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;

namespace Tests;

/// <summary>
/// The replaying <c>RunAsync(connectionFactory, …)</c> against SQLite's real lock conflict: a writer that holds the
/// database past the busy timeout makes the first attempt fail with <c>SQLITE_BUSY</c>, which the default
/// classification replays on a fresh connection.
/// </summary>
[Collection<SqliteUnitOfWorkFixture>]
public sealed class SqliteConnectionFactoryReplayTests(SqliteUnitOfWorkFixture fixture) : TestBase
{
    [Fact]
    public async Task should_replay_a_busy_database_on_a_fresh_connection_with_the_default_classification()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = SqliteUnitOfWorkFixture.BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var impatient = new SqliteConnectionStringBuilder(fixture.ConnectionString) { DefaultTimeout = 1 }.ToString();
        var connections = 0;
        await using var blocker = new SqliteConnection(fixture.ConnectionString);
        await blocker.OpenAsync(AbortToken);
        await using var holding = (SqliteTransaction)await blocker.BeginTransactionAsync(AbortToken);
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), AbortToken);
                await holding.RollbackAsync(AbortToken);
            },
            AbortToken
        );

        await factory.RunAsync(
            _ =>
            {
                Interlocked.Increment(ref connections);

                return ValueTask.FromResult(new SqliteConnection(impatient));
            },
            (unit, connection, ct) =>
                SqliteUnitOfWorkFixture.InsertProbeRowAsync(
                    connection,
                    (SqliteTransaction)((IRelationalUnitOfWorkResource)unit.Resource!).Transaction,
                    "replayed",
                    ct
                ),
            retry: new RetryStrategyOptions
            {
                ShouldHandle = UnitOfWorkRetryOptions.DefaultShouldHandle,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Constant,
            },
            cancellationToken: AbortToken
        );
        await release;

        connections.Should().BeGreaterThan(1, "the busy first attempt was replayed on a new connection");
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_a_permanent_fault_with_the_default_classification()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = SqliteUnitOfWorkFixture.BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var attempts = 0;

        var act = () =>
            factory.RunAsync(
                _ => ValueTask.FromResult(new SqliteConnection(fixture.ConnectionString)),
                async (_, connection, ct) =>
                {
                    Interlocked.Increment(ref attempts);
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT * FROM missing_table";
                    await command.ExecuteNonQueryAsync(ct);
                },
                retry: new RetryStrategyOptions
                {
                    ShouldHandle = UnitOfWorkRetryOptions.DefaultShouldHandle,
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.Zero,
                },
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<SqliteException>();
        attempts.Should().Be(1);
    }
}
