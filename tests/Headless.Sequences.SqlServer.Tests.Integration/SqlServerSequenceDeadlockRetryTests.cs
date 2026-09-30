// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// An autonomous increment chosen as a real deadlock victim (error 1205) waits a jittered delay on the store's clock
/// before it retries in a fresh transaction.
/// </summary>
/// <remarks>
/// SQL Server refuses to raise error numbers below 13000 from T-SQL, so unlike the PostgreSQL tests the failure cannot
/// be injected; the test builds a real lock cycle instead. A trigger makes the store's update wait on a gate row the
/// test's session holds, and the test's session then waits on the counter row the store's update holds. The test's
/// session runs at a higher deadlock priority, so the store's transaction is the victim.
/// </remarks>
[Collection<SqlServerSequencesFixture>]
public sealed class SqlServerSequenceDeadlockRetryTests(SqlServerSequencesFixture fixture) : TestBase
{
    private const string _Schema = "sequences_mssql_deadlock";
    private const string _Table = "Sequences";
    private const string _Gate = $"[{_Schema}].[deadlock_gate]";

    [Fact]
    public async Task should_wait_on_the_clock_before_retrying_a_deadlock_victim_then_succeed()
    {
        // given: a counter row the store's next increment will update
        var clock = new FakeTimeProvider();
        await using var services = await _CreateServicesAsync(clock);
        var store = services.GetRequiredService<ISequenceStore>();
        var key = new SequenceKey("", "deadlock", "");
        (await store.IncrementAsync(key, 1, 1, AbortToken)).Should().Be(1);
        await _InstallGateAsync();

        // and: the test's session holds the gate row
        await using var holder = new SqlConnection(fixture.CountersConnectionString);
        await holder.OpenAsync(AbortToken);
        var holderSession = await _ScalarAsync<short>(
            holder,
            null,
            "SET DEADLOCK_PRIORITY HIGH; SELECT @@SPID;",
            AbortToken
        );
        await using var holderTransaction = (SqlTransaction)await holder.BeginTransactionAsync(AbortToken);
        await _ScalarAsync<int>(
            holder,
            holderTransaction,
            $"UPDATE {_Gate} SET held = 1 WHERE id = 1; SELECT 1;",
            AbortToken
        );

        // when: the store's update takes the counter row and its trigger waits on the gate
        var increment = store.IncrementAsync(key, 1, 1, AbortToken).AsTask();
        await _WaitUntilBlockedByAsync(holderSession);

        // and: the test's session closes the cycle by waiting on the counter row, and survives as the higher priority
        await _ScalarAsync<long>(
            holder,
            holderTransaction,
            $"SELECT [Value] FROM [{_Schema}].[{_Table}] WITH (UPDLOCK) WHERE [Name] = N'deadlock';",
            AbortToken
        );

        await holderTransaction.CommitAsync(AbortToken);

        // then: the victim waits on the frozen clock instead of retrying at once
        await Task.Delay(TimeSpan.FromMilliseconds(300), AbortToken);
        increment.IsCompleted.Should().BeFalse();

        // and: once the clock moves past the delay, the retry runs in a fresh transaction and succeeds
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (!increment.IsCompleted)
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        (await increment).Should().Be(2);
    }

    private async Task<ServiceProvider> _CreateServicesAsync(FakeTimeProvider clock)
    {
        await fixture.ExecuteAsync(
            $"IF OBJECT_ID(N'{_Schema}.deadlock_gate', N'U') IS NOT NULL DROP TABLE {_Gate};"
                + SqlServerSequencesFixture.DropSchemaSql(_Schema, _Table),
            AbortToken
        );

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        fixture.ConfigureUnitOfWork(services);
        services.AddHeadlessSequences(setup =>
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = fixture.CountersConnectionString;
                options.Schema = _Schema;
            })
        );

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        return provider;
    }

    private async Task _InstallGateAsync()
    {
        await fixture.ExecuteAsync(
            $"CREATE TABLE {_Gate} (id int NOT NULL PRIMARY KEY, held bit NOT NULL); "
                + $"INSERT INTO {_Gate} (id, held) VALUES (1, 0);",
            AbortToken
        );
        await fixture.ExecuteAsync(
            $"""
            CREATE TRIGGER [{_Schema}].[deadlock_gate_wait] ON [{_Schema}].[{_Table}] AFTER INSERT, UPDATE AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @held bit;
                SELECT @held = held FROM {_Gate} WITH (UPDLOCK, ROWLOCK) WHERE id = 1;
            END
            """,
            AbortToken
        );
    }

    private async Task _WaitUntilBlockedByAsync(short session)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        await using var monitor = new SqlConnection(fixture.CountersConnectionString);
        await monitor.OpenAsync(timeout.Token);

        while (
            await _ScalarAsync<int>(
                monitor,
                null,
                $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {session};",
                timeout.Token
            ) == 0
        )
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private async Task<T> _ScalarAsync<T>(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        CancellationToken cancellationToken = default
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = 60;

        return (T)(await command.ExecuteScalarAsync(cancellationToken == default ? AbortToken : cancellationToken))!;
    }
}
