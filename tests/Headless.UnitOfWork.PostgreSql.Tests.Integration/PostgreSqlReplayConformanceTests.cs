// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Runs the replay conformance suite against <c>RunAsync(NpgsqlConnection, …)</c>, which never replays.</summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlConnectionReplayConformanceTests(PostgreSqlUnitOfWorkFixture fixture)
    : UnitOfWorkReplayConformanceTests(new PostgreSqlConnectionReplayFixture(fixture))
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

/// <summary>Runs the replay conformance suite against <c>RunAsync(DbContext, …)</c> over Npgsql.</summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlEntityFrameworkReplayConformanceTests(PostgreSqlUnitOfWorkFixture fixture)
    : UnitOfWorkReplayConformanceTests(
        new EntityFrameworkReplayFixture(fixture, options => options.UseNpgsql(fixture.ConnectionString))
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
/// <c>RunAsync(NpgsqlConnection, …)</c> on a caller-owned connection. The commit fault is a real server error: a
/// deferred unique constraint that the armed attempt violates, which PostgreSQL reports only when the commit runs.
/// </summary>
public sealed class PostgreSqlConnectionReplayFixture(PostgreSqlUnitOfWorkFixture container) : IUnitOfWorkReplayFixture
{
    public bool ReplaysBeforeCommit => false;

    public async Task<TResult> RunAsync<TResult>(
        Func<IUnitOfWorkReplayContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = PostgreSqlUnitOfWorkFixture.BuildProvider();
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection(container.ConnectionString);

        return await factory.RunAsync(
            connection,
            (unitOfWork, ct) => operation(new PostgreSqlReplayContext(factory, connection, unitOfWork), ct),
            cancellationToken: cancellationToken
        );
    }

    public Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        return container.CountProbeRowsAsync(cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await container.ResetAsync(cancellationToken);
        await using var connection = new NpgsqlConnection(container.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS replay_commit_guard (
                id int,
                CONSTRAINT replay_commit_guard_id_key UNIQUE (id) DEFERRABLE INITIALLY DEFERRED
            );
            DELETE FROM replay_commit_guard;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>One attempt of a raw-ADO PostgreSQL <c>RunAsync</c>, on the connection that attempt runs on.</summary>
public sealed class PostgreSqlReplayContext(
    IUnitOfWorkFactory factory,
    NpgsqlConnection connection,
    IUnitOfWork unitOfWork
) : IUnitOfWorkReplayContext
{
    public IUnitOfWork UnitOfWork => unitOfWork;

    public Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
    {
        return PostgreSqlUnitOfWorkFixture.InsertProbeRowAsync(connection, _Transaction(), name, cancellationToken);
    }

    public async Task ArmCommitFaultAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO replay_commit_guard (id) VALUES (1), (1)",
            connection,
            _Transaction()
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task RunJoinedAsync(
        Func<IUnitOfWork, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        return factory.RunAsync(connection, operation, cancellationToken: cancellationToken);
    }

    private NpgsqlTransaction _Transaction()
    {
        return (NpgsqlTransaction?)(unitOfWork.Resource as IRelationalUnitOfWorkResource)?.Transaction
            ?? throw new InvalidOperationException("The unit exposed no live relational transaction.");
    }
}
