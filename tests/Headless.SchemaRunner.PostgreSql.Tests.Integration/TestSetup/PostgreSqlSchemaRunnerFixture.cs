// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Idempotency;
using Headless.Sequences;
using Headless.Sql.PostgreSql;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Tests.TestSetup;

/// <summary>
/// PostgreSQL fixture for the schema runner suite. Each runner comes from its own service provider registering the
/// real Sequences, Idempotency, and Coordination providers, so one runner stands for one replica. Tests run serially
/// because the measurement and race scenarios observe the whole database.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlSchemaRunnerFixture
    : HeadlessPostgreSqlFixture,
        ICollectionFixture<PostgreSqlSchemaRunnerFixture>,
        ISchemaRunnerFixture
{
    public ISchemaDialect Dialect => PostgreSqlSchemaDialect.Instance;

    // Sequences 1, Idempotency 1, Coordination 1.
    public int ExpectedStepCount => 3;

    // sequences, idempotency_records, three coordination tables, and the history table.
    public int ExpectedTableCount => 6;

    public bool ForeignCreatorForcesRerun => true;

    // Each replica gets its own service provider but they share the pool; cap it so five replicas stay far below the
    // container's max_connections.
    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { MaxPoolSize = 20 }.ToString();

    protected override PostgreSqlBuilder Configure()
    {
        return base.Configure().WithDatabase("schema_runner_test").WithUsername("postgres").WithPassword("postgres");
    }

    public SchemaRunner CreateRunner(string schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _AddPilotFeatures(services, schema);

        return services.BuildServiceProvider().GetRequiredService<SchemaRunner>();
    }

    public IHost CreateHost(string schema, bool setupResources)
    {
        // The Test environment, so the conformance suite may clear and tear down; the builder's default is Production.
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Headless.EnvironmentNames.Test }
        );
        // The pilot features' contributions alone, so the runner and the resource setup are the host's only hosted
        // services, and the setup sees the contributions the runner verifies.
        foreach (var contribution in CreateRunner(schema).Contributions)
        {
            builder.Services.AddHeadlessSchemaContribution(_ => contribution);
        }

        if (setupResources)
        {
            builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        }

        return builder.Build();
    }

    public Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        return _ExecuteAsync($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""", cancellationToken);
    }

    public async Task ExecuteScriptWithPlainClientAsync(string script, CancellationToken cancellationToken)
    {
        // psql inside the container: the client a DBA would use, with no Headless code on the path.
        var result = await Container.ExecScriptAsync(script, cancellationToken);

        result.ExitCode.Should().Be(0, result.Stderr);
        result
            .Stderr.Should()
            .NotContain("ERROR", "psql reports statement errors on stderr without failing the exit code");
    }

    public async Task<IForeignCreator> BeginForeignCreatorAsync(string schema, CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // False positive: the returned ForeignCreator owns and disposes the connection.
        var connection = new NpgsqlConnection(ConnectionString);
#pragma warning restore CA2000
        await connection.OpenAsync(cancellationToken);

        await using var pidCommand = new NpgsqlCommand("SELECT pg_backend_pid();", connection);
        var pid = (int)(await pidCommand.ExecuteScalarAsync(cancellationToken))!;

        var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // What an EF migration owning the Sequences table would run: the schema, then the table, uncommitted.
        await using var create = new NpgsqlCommand(
            $"""
            CREATE SCHEMA "{schema}";
            CREATE TABLE "{schema}"."sequences" (
                tenant_id varchar(128) NOT NULL,
                name varchar(128) NOT NULL,
                "partition" varchar(64) NOT NULL,
                value bigint NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL,
                CONSTRAINT "pk_sequences" PRIMARY KEY (tenant_id, name, "partition")
            );
            """,
            connection,
            transaction
        );
        await create.ExecuteNonQueryAsync(cancellationToken);

        return new ForeignCreator(ConnectionString, connection, transaction, pid);
    }

    public Task TamperChecksumAsync(string schema, string feature, string version, CancellationToken cancellationToken)
    {
        return _ExecuteAsync(
            $"""
            UPDATE "{schema}".headless_schema_history SET checksum = repeat('0', 64)
            WHERE feature = '{feature}' AND step_version = '{version}';
            """,
            cancellationToken
        );
    }

    public Task DeleteHistoryRowAsync(
        string schema,
        string feature,
        string version,
        CancellationToken cancellationToken
    )
    {
        return _ExecuteAsync(
            $"""
            DELETE FROM "{schema}".headless_schema_history WHERE feature = '{feature}' AND step_version = '{version}';
            """,
            cancellationToken
        );
    }

    public Task<int> CountHistoryRowsAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync($"""SELECT count(*) FROM "{schema}".headless_schema_history;""", cancellationToken);
    }

    public Task<int> CountTablesAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync(
            $"""
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = '{schema}' AND table_type = 'BASE TABLE';
            """,
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<string>> ListObjectsAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = '{schema}' AND c.relkind IN ('r', 'p', 'S');
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public Task AddForeignStateAsync(string schema, CancellationToken cancellationToken)
    {
        return _ExecuteAsync(
            $"""
            CREATE TABLE "{schema}"."{ISchemaRunnerFixture.ForeignTable}" (id int PRIMARY KEY);
            INSERT INTO "{schema}"."{ISchemaRunnerFixture.ForeignTable}" (id) VALUES (1);
            INSERT INTO "{schema}".headless_schema_history (feature, step_version, description, checksum)
            VALUES ('{ISchemaRunnerFixture.ForeignFeature}', '1', 'foreign', '{new string('0', 64)}');
            """,
            cancellationToken
        );
    }

    public Task<int> CountForeignRowsAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync(
            $"""SELECT count(*) FROM "{schema}"."{ISchemaRunnerFixture.ForeignTable}";""",
            cancellationToken
        );
    }

    public Task<int> CountHistoryRowsAsync(string schema, string feature, CancellationToken cancellationToken)
    {
        return _ScalarAsync(
            $"""SELECT count(*) FROM "{schema}".headless_schema_history WHERE feature = '{feature}';""",
            cancellationToken
        );
    }

    public async Task RunLegacyInitializerProtocolAsync(string schema, CancellationToken cancellationToken)
    {
        // The deleted initializers, one after another as the host ran them: each opened its own connection, took its
        // feature lock and the schema lock inside its own transaction, and re-ran its whole idempotent DDL batch.
        foreach (var contribution in CreateRunner(schema).Contributions)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var batch = $"""
                SELECT pg_advisory_xact_lock(hashtextextended('headless_{contribution.Feature.ToLowerInvariant()}_init:{schema}', 0));
                {PostgreSqlSchemaInitLock.AcquireStatement(schema)}
                CREATE SCHEMA IF NOT EXISTS "{schema}";
                {string.Join(Environment.NewLine, contribution.Steps.Select(s => s.Sql))}
                """;

            await using var command = new NpgsqlCommand(batch, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private void _AddPilotFeatures(IServiceCollection services, string schema)
    {
        var connectionString = ConnectionString;

        services.AddHeadlessSequences(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = connectionString;
                options.Schema = schema;
            })
        );
        services.AddHeadlessIdempotency(setup =>
        {
            setup.UsePostgreSql(connectionString);
            setup.ConfigureStorage(options => options.Schema = schema);
        });
        services.AddHeadlessCoordination(setup =>
        {
            setup.UsePostgreSql(options => options.ConnectionString = connectionString);
            setup.ConfigureStorage(options => options.Schema = schema);
        });
    }

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> _ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private sealed class ForeignCreator(
        string connectionString,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int pid
    ) : IForeignCreator
    {
        private bool _committed;

        public async Task<bool> IsBlockingAnotherSessionAsync(CancellationToken cancellationToken)
        {
            // Polled from its own autocommit connection: pg_stat_activity is snapshotted once per transaction, so a
            // poll inside the foreign transaction would never see the waiter.
            await using var probe = new NpgsqlConnection(connectionString);
            await probe.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE @pid = ANY (pg_blocking_pids(pid)));",
                probe
            );
            command.Parameters.AddWithValue("pid", pid);

            return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
