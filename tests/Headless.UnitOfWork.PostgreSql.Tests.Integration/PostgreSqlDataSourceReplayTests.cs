// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Polly.Retry;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// <c>RunAsync(NpgsqlDataSource, …)</c> against real server errors under the framework's default classification: a
/// serialization failure is what a <c>Serializable</c> workload is expected to replay, and a constraint violation
/// is a permanent fault that no replay can cure.
/// </summary>
[Collection<PostgreSqlUnitOfWorkFixture>]
public sealed class PostgreSqlDataSourceReplayTests(PostgreSqlUnitOfWorkFixture fixture) : TestBase
{
    private const string _RaiseSerializationFailure =
        "DO $$ BEGIN RAISE EXCEPTION 'simulated conflict' USING ERRCODE = 'serialization_failure'; END $$";

    private const string _RaiseUniqueViolation =
        "DO $$ BEGIN RAISE EXCEPTION 'simulated duplicate' USING ERRCODE = 'unique_violation'; END $$";

    [Fact]
    public async Task should_replay_a_serialization_failure_on_a_fresh_connection_with_the_default_classification()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider(configureReplay: true);
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var connections = new List<NpgsqlConnection>();

        var result = await factory.RunAsync(
            dataSource,
            async (unitOfWork, connection, ct) =>
            {
                connections.Add(connection);
                await _InsertProbeRowAsync(unitOfWork, connection, $"attempt-{connections.Count}", ct);

                if (connections.Count == 1)
                {
                    await _ExecuteAsync(unitOfWork, connection, _RaiseSerializationFailure, ct);
                }

                return connections.Count;
            },
            IsolationLevel.Serializable,
            cancellationToken: AbortToken
        );

        result.Should().Be(2, "SQLSTATE 40001 is transient under DefaultShouldHandle");
        connections[1].Should().NotBeSameAs(connections[0], "each attempt opens its own connection");
        connections.Should().OnlyContain(connection => connection.State == ConnectionState.Closed);
        (await fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_a_permanent_fault_with_the_default_classification()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider(configureReplay: true);
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var attempts = 0;

        PostgresException? thrown = null;

        try
        {
            await factory.RunAsync(
                dataSource,
                async (unitOfWork, connection, ct) =>
                {
                    attempts++;
                    await _ExecuteAsync(unitOfWork, connection, _RaiseUniqueViolation, ct);
                },
                cancellationToken: AbortToken
            );
        }
        catch (PostgresException ex)
        {
            thrown = ex;
        }

        thrown.Should().NotBeNull();
        thrown!.SqlState.Should().Be("23505");
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_when_neither_the_host_nor_the_call_turns_replay_on()
    {
        await fixture.ResetAsync(AbortToken);
        await using var provider = _BuildProvider(configureReplay: false);
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString);
        var attempts = 0;

        PostgresException? thrown = null;

        try
        {
            await factory.RunAsync(
                dataSource,
                async (unitOfWork, connection, ct) =>
                {
                    attempts++;
                    await _ExecuteAsync(unitOfWork, connection, _RaiseSerializationFailure, ct);
                },
                cancellationToken: AbortToken
            );
        }
        catch (PostgresException ex)
        {
            thrown = ex;
        }

        thrown.Should().NotBeNull();
        thrown!.SqlState.Should().Be("40001");
        attempts.Should().Be(1, "replay is off by default");
    }

    private static ServiceProvider _BuildProvider(bool configureReplay)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlUnitOfWork();

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

    private static Task _InsertProbeRowAsync(
        IUnitOfWork unitOfWork,
        NpgsqlConnection connection,
        string name,
        CancellationToken cancellationToken
    )
    {
        return PostgreSqlUnitOfWorkFixture.InsertProbeRowAsync(
            connection,
            (NpgsqlTransaction)((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Transaction,
            name,
            cancellationToken
        );
    }

    private static async Task _ExecuteAsync(
        IUnitOfWork unitOfWork,
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            sql,
            connection,
            (NpgsqlTransaction)((IRelationalUnitOfWorkResource)unitOfWork.Resource!).Transaction
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
