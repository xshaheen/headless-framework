// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the replay conformance suite against <c>RunAsync(SqlConnection, …)</c>, which never replays.</summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerConnectionReplayConformanceTests(SqlServerUnitOfWorkFixture fixture)
    : UnitOfWorkReplayConformanceTests(new SqlServerConnectionReplayFixture(fixture))
{
    [Fact]
    public override Task should_replay_a_fault_before_the_commit_only_when_the_spelling_replays()
    {
        return base.should_replay_a_fault_before_the_commit_only_when_the_spelling_replays();
    }

    [Fact]
    public override Task should_not_replay_when_the_commit_faults()
    {
        return base.should_not_replay_when_the_commit_faults();
    }

    [Fact]
    public override Task should_report_an_in_doubt_commit_without_replay_when_the_connection_fails_during_the_commit()
    {
        return base.should_report_an_in_doubt_commit_without_replay_when_the_connection_fails_during_the_commit();
    }

    [Fact]
    public override Task should_not_replay_after_the_block_prevents_retry()
    {
        return base.should_not_replay_after_the_block_prevents_retry();
    }

    [Fact]
    public override Task should_return_the_result_when_the_drain_faults_after_a_durable_commit()
    {
        return base.should_return_the_result_when_the_drain_faults_after_a_durable_commit();
    }

    [Fact]
    public override Task should_replay_a_joined_block_only_with_its_owner()
    {
        return base.should_replay_a_joined_block_only_with_its_owner();
    }
}

/// <summary>Runs the replay conformance suite against <c>RunAsync(DbContext, …)</c> over SqlClient.</summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerEntityFrameworkReplayConformanceTests(SqlServerUnitOfWorkFixture fixture)
    : UnitOfWorkReplayConformanceTests(
        new EntityFrameworkReplayFixture(
            fixture,
            options => options.UseSqlServer(fixture.ConnectionString),
            (connection, ct) =>
                SqlServerUnitOfWorkFixture.KillSessionAsync(
                    fixture.ConnectionString,
                    ((SqlConnection)connection).ServerProcessId,
                    ct
                )
        )
    )
{
    [Fact]
    public override Task should_replay_a_fault_before_the_commit_only_when_the_spelling_replays()
    {
        return base.should_replay_a_fault_before_the_commit_only_when_the_spelling_replays();
    }

    [Fact]
    public override Task should_not_replay_when_the_commit_faults()
    {
        return base.should_not_replay_when_the_commit_faults();
    }

    [Fact]
    public override Task should_report_an_in_doubt_commit_without_replay_when_the_connection_fails_during_the_commit()
    {
        return base.should_report_an_in_doubt_commit_without_replay_when_the_connection_fails_during_the_commit();
    }

    [Fact]
    public override Task should_not_replay_after_the_block_prevents_retry()
    {
        return base.should_not_replay_after_the_block_prevents_retry();
    }

    [Fact]
    public override Task should_return_the_result_when_the_drain_faults_after_a_durable_commit()
    {
        return base.should_return_the_result_when_the_drain_faults_after_a_durable_commit();
    }

    [Fact]
    public override Task should_replay_a_joined_block_only_with_its_owner()
    {
        return base.should_replay_a_joined_block_only_with_its_owner();
    }
}

/// <summary>
/// Runs the replay conformance suite against <c>RunAsync(Func&lt;CancellationToken, ValueTask&lt;SqlConnection&gt;&gt;, …)</c>
/// with the replay policy passed per call.
/// </summary>
[Collection<SqlServerUnitOfWorkFixture>]
public sealed class SqlServerConnectionFactoryReplayConformanceTests(SqlServerUnitOfWorkFixture fixture)
    : UnitOfWorkReplayConformanceTests(new SqlServerConnectionFactoryReplayFixture(fixture))
{
    [Fact]
    public override Task should_replay_a_fault_before_the_commit_only_when_the_spelling_replays()
    {
        return base.should_replay_a_fault_before_the_commit_only_when_the_spelling_replays();
    }

    [Fact]
    public override Task should_not_replay_when_the_commit_faults()
    {
        return base.should_not_replay_when_the_commit_faults();
    }

    [Fact]
    public override Task should_report_an_in_doubt_commit_without_replay_when_the_connection_fails_during_the_commit()
    {
        return base.should_report_an_in_doubt_commit_without_replay_when_the_connection_fails_during_the_commit();
    }

    [Fact]
    public override Task should_not_replay_after_the_block_prevents_retry()
    {
        return base.should_not_replay_after_the_block_prevents_retry();
    }

    [Fact]
    public override Task should_return_the_result_when_the_drain_faults_after_a_durable_commit()
    {
        return base.should_return_the_result_when_the_drain_faults_after_a_durable_commit();
    }

    [Fact]
    public override Task should_replay_a_joined_block_only_with_its_owner()
    {
        return base.should_replay_a_joined_block_only_with_its_owner();
    }
}

/// <summary>
/// <c>RunAsync(Func&lt;CancellationToken, ValueTask&lt;SqlConnection&gt;&gt;, …)</c>, with the replay policy passed on
/// the call and no host default: replay every fault once with no delay, so a scenario proves that a refusal comes
/// from the runner's policy and not from the classification. The factory hands out closed connections, which the
/// runner opens.
/// </summary>
public sealed class SqlServerConnectionFactoryReplayFixture(SqlServerUnitOfWorkFixture container)
    : IUnitOfWorkReplayFixture
{
    public bool ReplaysBeforeCommit => true;

    public async Task<TResult> RunAsync<TResult>(
        Func<IUnitOfWorkReplayContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();

        return await factory.RunAsync(
            _ => ValueTask.FromResult(new SqlConnection(container.ConnectionString)),
            (unitOfWork, connection, ct) =>
                operation(new SqlServerReplayContext(factory, connection, unitOfWork, container.ConnectionString), ct),
            retry: new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is not null),
            },
            cancellationToken: cancellationToken
        );
    }

    public Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        return container.CountProbeRowsAsync(cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        return container.ResetAsync(cancellationToken);
    }
}

/// <summary>
/// <c>RunAsync(SqlConnection, …)</c> on a caller-owned connection. SQL Server has no deferred constraints, so the
/// armed attempt ends its transaction on the server behind the unit's back and the commit then faults in SqlClient.
/// </summary>
public sealed class SqlServerConnectionReplayFixture(SqlServerUnitOfWorkFixture container) : IUnitOfWorkReplayFixture
{
    public bool ReplaysBeforeCommit => false;

    public async Task<TResult> RunAsync<TResult>(
        Func<IUnitOfWorkReplayContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = SqlServerUnitOfWorkFixture.BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(container.ConnectionString);

        return await factory.RunAsync(
            connection,
            (unitOfWork, ct) =>
                operation(new SqlServerReplayContext(factory, connection, unitOfWork, container.ConnectionString), ct),
            cancellationToken: cancellationToken
        );
    }

    public Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        return container.CountProbeRowsAsync(cancellationToken);
    }

    public Task ResetAsync(CancellationToken cancellationToken)
    {
        return container.ResetAsync(cancellationToken);
    }
}

/// <summary>One attempt of a raw-ADO SQL Server <c>RunAsync</c>, on the connection that attempt runs on.</summary>
public sealed class SqlServerReplayContext(
    IUnitOfWorkFactory factory,
    SqlConnection connection,
    IUnitOfWork unitOfWork,
    string adminConnectionString
) : IUnitOfWorkReplayContext
{
    public IUnitOfWork UnitOfWork => unitOfWork;

    public Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
    {
        return SqlServerUnitOfWorkFixture.InsertProbeRowAsync(connection, _Transaction(), name, cancellationToken);
    }

    public async Task ArmCommitFaultAsync(CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("ROLLBACK TRANSACTION", connection, _Transaction());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task BreakConnectionAsync(CancellationToken cancellationToken)
    {
        return SqlServerUnitOfWorkFixture.KillSessionAsync(
            adminConnectionString,
            connection.ServerProcessId,
            cancellationToken
        );
    }

    public Task RunJoinedAsync(
        Func<IUnitOfWork, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        return factory.RunAsync(connection, operation, cancellationToken: cancellationToken);
    }

    private SqlTransaction _Transaction()
    {
        return (SqlTransaction?)(unitOfWork.Resource as IRelationalUnitOfWorkResource)?.Transaction
            ?? throw new InvalidOperationException("The unit exposed no live relational transaction.");
    }
}
