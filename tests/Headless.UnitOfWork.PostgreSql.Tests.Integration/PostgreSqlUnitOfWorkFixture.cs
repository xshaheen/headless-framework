// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Testcontainers;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests;

/// <summary>
/// PostgreSQL leaf fixture for the <c>RunAsync</c> conformance suite and the provider-specific scenarios. The
/// probe table is created outside any unit of work (transactional DDL would vanish on rollback) and probe
/// counting uses an independent connection so it observes committed state only. The tests share one probe table,
/// so the collection runs serially.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlUnitOfWorkFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlUnitOfWorkFixture>,
        IUnitOfWorkRunFixture
{
    public string ConnectionString => Container.GetConnectionString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("unit_of_work_test").WithUsername("postgres").WithPassword("postgres");
    }

    public async Task RunAsync(
        Func<IUnitOfWorkRunContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var connection = new NpgsqlConnection(ConnectionString);

        await manager.RunAsync(
            connection,
            (unitOfWork, ct) => operation(new PostgreSqlRunContext(connection, unitOfWork), ct),
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

    /// <summary>Inserts one probe row on <paramref name="transaction" />; shared by the provider-specific scenarios.</summary>
    public static async Task InsertProbeRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string name,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO probe_rows (name) VALUES (@name)",
            connection,
            transaction
        );
        command.Parameters.AddWithValue(nameof(name), name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static ServiceProvider BuildProvider(CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (logs is not null)
            {
                builder.AddProvider(logs);
            }
        });
        services.AddPostgreSqlUnitOfWork();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class PostgreSqlRunContext(NpgsqlConnection connection, IUnitOfWork unitOfWork)
        : IUnitOfWorkRunContext
    {
        public IUnitOfWork UnitOfWork => unitOfWork;

        public Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            // Reach the live transaction through the relational resource — the same designed path production
            // participants (the outbox writer, the job writer) use to enlist their rows.
            var transaction =
                (NpgsqlTransaction?)(unitOfWork.Resource as IRelationalUnitOfWorkResource)?.Transaction
                ?? throw new InvalidOperationException("The helper exposed no live relational transaction.");

            return PostgreSqlUnitOfWorkFixture.InsertProbeRowAsync(connection, transaction, name, cancellationToken);
        }
    }
}
