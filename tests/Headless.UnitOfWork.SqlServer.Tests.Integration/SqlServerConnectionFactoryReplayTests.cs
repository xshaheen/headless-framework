// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;

namespace Tests;

/// <summary>
/// <c>RunAsync(Func&lt;CancellationToken, ValueTask&lt;SqlConnection&gt;&gt;, …)</c> against real SqlClient faults under
/// the framework's default classification. A lock-request timeout (1222) is a real <c>SqlException</c> that the
/// widened SQL Server set replays and that a test can raise on demand: another session holds the row, and the
/// attempt gives up after <c>SET LOCK_TIMEOUT</c>. It proves the type-name match, the walk over
/// <c>SqlException.Errors</c>, and the replay on a fresh connection against the shipped driver, which the unit tests
/// cover only with a same-named fake. A permanent fault raised by <c>THROW</c> is never replayed.
/// </summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerConnectionFactoryReplayTests(SqlServerUnitOfWorkFixture fixture) : TestBase
{
    private const int _LockRequestTimeoutNumber = 1222;

    private const string _EnsureLockProbe =
        "IF OBJECT_ID('dbo.replay_lock_probe', 'U') IS NULL CREATE TABLE dbo.replay_lock_probe (id int PRIMARY KEY, v int NOT NULL); "
        + "IF NOT EXISTS (SELECT 1 FROM dbo.replay_lock_probe WHERE id = 1) INSERT INTO dbo.replay_lock_probe (id, v) VALUES (1, 0);";

    private const string _TouchLockProbe = "UPDATE dbo.replay_lock_probe SET v = v + 1 WHERE id = 1";

    [Fact]
    public async Task should_replay_a_real_lock_timeout_on_a_fresh_connection_with_the_default_classification()
    {
        await fixture.ResetAsync(AbortToken);
        await _EnsureLockProbeAsync();
        await using var provider = _BuildProvider(configureReplay: true);
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var connections = new List<SqlConnection>();
        SqlException? firstAttemptFault = null;
        var firstAttemptFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holderReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Another session holds the probe row for as long as the first attempt needs to time out on it.
        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync(AbortToken);
        var holding = (SqlTransaction)await holder.BeginTransactionAsync(AbortToken);
        await using (var hold = new SqlCommand(_TouchLockProbe, holder, holding))
        {
            await hold.ExecuteNonQueryAsync(AbortToken);
        }

        var release = Task.Run(
            async () =>
            {
                await firstAttemptFailed.Task;
                await holding.RollbackAsync(AbortToken);
                holderReleased.SetResult();
            },
            AbortToken
        );

        var result = await factory.RunAsync(
            _ => ValueTask.FromResult(new SqlConnection(fixture.ConnectionString)),
            async (unitOfWork, connection, ct) =>
            {
                connections.Add(connection);
                var transaction = (SqlTransaction)((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Transaction;

                if (connections.Count == 1)
                {
                    unitOfWork.OnFailed(_ =>
                    {
                        firstAttemptFailed.TrySetResult();

                        return ValueTask.CompletedTask;
                    });

                    await using var timeout = new SqlCommand("SET LOCK_TIMEOUT 500", connection, transaction);
                    await timeout.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    await holderReleased.Task.WaitAsync(ct);
                }

                await SqlServerUnitOfWorkFixture.InsertProbeRowAsync(
                    connection,
                    transaction,
                    $"attempt-{connections.Count}",
                    ct
                );

                try
                {
                    await using var touch = new SqlCommand(_TouchLockProbe, connection, transaction);
                    await touch.ExecuteNonQueryAsync(ct);
                }
                catch (SqlException ex) when (connections.Count == 1)
                {
                    firstAttemptFault = ex;

                    throw;
                }

                return connections.Count;
            },
            cancellationToken: AbortToken
        );

        await release;
        result.Should().Be(2, "a lock-request timeout is in the SQL Server set DefaultShouldHandle replays");
        firstAttemptFault.Should().NotBeNull();
        firstAttemptFault!.Number.Should().Be(_LockRequestTimeoutNumber);
        (await _DefaultShouldHandleAsync(firstAttemptFault))
            .Should()
            .BeTrue("the real SqlException is classified by type name and error number");
        connections[1].Should().NotBeSameAs(connections[0], "each attempt opens its own connection");
        connections.Should().OnlyContain(connection => connection.State == System.Data.ConnectionState.Closed);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "the timed-out attempt's row rolled back");
    }

    [Fact]
    public async Task should_not_replay_a_permanent_fault_with_the_default_classification()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider(configureReplay: true);
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var attempts = 0;

        var act = () =>
            factory.RunAsync(
                _ => ValueTask.FromResult(new SqlConnection(fixture.ConnectionString)),
                async (unitOfWork, connection, ct) =>
                {
                    attempts++;
                    await using var command = new SqlCommand(
                        "THROW 50001, 'simulated permanent fault', 1;",
                        connection,
                        (SqlTransaction)((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Transaction
                    );
                    await command.ExecuteNonQueryAsync(ct);
                },
                cancellationToken: AbortToken
            );

        var thrown = (await act.Should().ThrowAsync<SqlException>()).Which;
        thrown.Number.Should().Be(50001);
        (await _DefaultShouldHandleAsync(thrown)).Should().BeFalse();
        attempts.Should().Be(1);
    }

    private async Task _EnsureLockProbeAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(_EnsureLockProbe, connection);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private static ServiceProvider _BuildProvider(bool configureReplay)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerUnitOfWork();

        if (configureReplay)
        {
            services.Configure<UnitOfWorkRetryOptions>(options =>
                options.RetryStrategy = new RetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.Zero,
                    ShouldHandle = UnitOfWorkRetryOptions.DefaultShouldHandle,
                }
            );
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async ValueTask<bool> _DefaultShouldHandleAsync(Exception exception)
    {
        var context = ResilienceContextPool.Shared.Get(CancellationToken.None);

        try
        {
            return await UnitOfWorkRetryOptions.DefaultShouldHandle(
                new RetryPredicateArguments<object>(context, Outcome.FromException<object>(exception), attemptNumber: 0)
            );
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
