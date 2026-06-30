// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.PostgreSql;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL leaf fixture for the coordinated-transaction conformance suite. Wraps the raw-ADO
/// <c>NpgsqlConnection.ExecuteCoordinatedTransactionAsync</c> helper — the inline (caller-driven) signal
/// provider where the original silent-work-loss P0 lived: the helper itself must signal
/// <c>Committed</c> after <c>CommitAsync</c>, and these scenarios fail if it ever stops doing so.
/// The probe table is created outside any coordinated transaction (transactional DDL would vanish on
/// rollback) and probe counting uses an independent connection so it observes committed state only.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlCoordinatedTransactionFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlCoordinatedTransactionFixture>,
        ICoordinatedTransactionFixture
{
    private string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure()
            .WithDatabase("commit_coordination_test")
            .WithUsername("postgres")
            .WithPassword("postgres");
    }

    public async Task RunCoordinatedAsync(
        Func<ICoordinatedTransactionContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = _BuildProvider();
        await using var connection = new NpgsqlConnection(ConnectionString);

        await connection.ExecuteCoordinatedTransactionAsync(
            (conn, ct) => operation(new PostgreSqlCoordinatedTransactionContext(provider, conn), ct),
            provider,
            cancellationToken: cancellationToken
        );
    }

    public async Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM probe_rows", connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS probe_rows (id serial PRIMARY KEY, name text); DELETE FROM probe_rows;",
            connection
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ServiceProvider _BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlCommitCoordination();

        return services.BuildServiceProvider();
    }

    private sealed class PostgreSqlCoordinatedTransactionContext(IServiceProvider services, NpgsqlConnection connection)
        : ICoordinatedTransactionContext
    {
        // Lazy on every read: the helper enlists per attempt.
        public ICommitCoordinator Coordinator =>
            services.GetRequiredService<ICurrentCommitCoordinator>().Current
            ?? throw new InvalidOperationException("No ambient coordinator — the helper did not enlist.");

        public async Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            // Reach the live transaction through the relational capability — the same designed path
            // production participants (e.g. the outbox writer) use to enlist their writes.
            if (!Coordinator.TryGetCapability<IRelationalCommitContext>(out var relational))
            {
                throw new InvalidOperationException("The helper did not attach IRelationalCommitContext.");
            }

            var transaction =
                (NpgsqlTransaction?)relational.Transaction
                ?? throw new InvalidOperationException("The relational capability exposed no live transaction.");

            await using var command = new NpgsqlCommand(
                "INSERT INTO probe_rows (name) VALUES (@name)",
                connection,
                transaction
            );
            command.Parameters.AddWithValue("name", name);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
