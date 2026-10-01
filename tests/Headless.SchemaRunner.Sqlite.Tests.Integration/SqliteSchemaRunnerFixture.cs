// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Coordination;
using Headless.Hosting.Initialization.Schema;
using Headless.Idempotency;
using Headless.Sequences;
using Headless.Sql.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// SQLite fixture for the schema runner suite, over one database file. A schema is a name prefix on SQLite, so every
/// scenario's objects are the tables named <c>&lt;schema&gt;_…</c>. Each runner comes from its own service provider
/// registering the real Sequences, Idempotency, and Coordination providers, so one runner stands for one replica.
/// Tests run serially because the measurement and race scenarios observe the whole database.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class SqliteSchemaRunnerFixture
    : ICollectionFixture<SqliteSchemaRunnerFixture>,
        ISchemaRunnerFixture,
        IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    public ISchemaDialect Dialect => SqliteSchemaDialect.Instance;

    // Sequences 1, Idempotency 1, Coordination 1.
    public int ExpectedStepCount => 3;

    // sequences, idempotency_records, the idempotency generation sequence (a one-row table on SQLite), three
    // coordination tables, and the history table.
    public int ExpectedTableCount => 7;

    // A transaction holds the database write lock from its first statement, so the runner's steps never see a
    // foreign creator half-way: they wait for its commit and then find the objects through their guards.
    public bool ForeignCreatorForcesRerun => false;

    private string ConnectionString => _database.ConnectionString;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    public SchemaRunner CreateRunner(string schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _AddPilotFeatures(services, schema);

        return services.BuildServiceProvider().GetRequiredService<SchemaRunner>();
    }

    public IHost CreateHost(string schema, SchemaRunnerMode mode)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(CreateRunner(schema));
        builder.Services.AddHeadlessSchemaRunner(options => options.Mode = mode);

        return builder.Build();
    }

    public async Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        var tables = new List<string>();

        await using (var list = connection.CreateCommand())
        {
            list.CommandText = $"SELECT name FROM sqlite_schema WHERE type = 'table' AND {_InSchema(schema)}";
            await using var reader = await list.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        // Dropping a table drops its indexes and triggers with it.
        foreach (var table in tables)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {SqliteDialect.Instance.Quote(table)}";
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task ExecuteScriptWithPlainClientAsync(string script, CancellationToken cancellationToken)
    {
        // The sqlite3 shell, the client a DBA would use, when the machine has it; otherwise the driver running the
        // whole file as one command, which is what the shell does too. Neither path runs Headless code.
        if (_FindSqliteShell() is { } shell)
        {
            using var process = Process.Start(
                new ProcessStartInfo(shell, ["-bail", new SqliteConnectionStringBuilder(ConnectionString).DataSource])
                {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                }
            )!;
            await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            process.ExitCode.Should().Be(0, stderr);

            return;
        }

        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IForeignCreator> BeginForeignCreatorAsync(string schema, CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // False positive: the returned ForeignCreator owns and disposes the connection.
        var connection = new SqliteConnection(ConnectionString);
#pragma warning restore CA2000
        await connection.OpenAsync(cancellationToken);
        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        // What an EF migration owning the Sequences table would run, uncommitted.
        await using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = $"""
            CREATE TABLE "{schema}_sequences" (
                tenant_id TEXT NOT NULL,
                name TEXT NOT NULL,
                "partition" TEXT NOT NULL,
                value INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                CONSTRAINT "pk_sequences" PRIMARY KEY (tenant_id, name, "partition")
            );
            """;
        await create.ExecuteNonQueryAsync(cancellationToken);

        return new ForeignCreator(ConnectionString, connection, transaction);
    }

    public Task TamperChecksumAsync(string schema, string feature, string version, CancellationToken cancellationToken)
    {
        return _ExecuteAsync(
            $"""
            UPDATE "{schema}_headless_schema_history" SET checksum = '{new string('0', 64)}'
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
            DELETE FROM "{schema}_headless_schema_history" WHERE feature = '{feature}' AND step_version = '{version}';
            """,
            cancellationToken
        );
    }

    public Task<int> CountHistoryRowsAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync($"""SELECT count(*) FROM "{schema}_headless_schema_history";""", cancellationToken);
    }

    public Task<int> CountTablesAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync(
            $"SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND {_InSchema(schema)}",
            cancellationToken
        );
    }

    public async Task RunLegacyInitializerProtocolAsync(string schema, CancellationToken cancellationToken)
    {
        // SQLite never had hand-written initializers; the closest equivalent re-runs each feature's whole idempotent
        // DDL in its own transaction on its own connection, which is what such an initializer would have done.
        foreach (var contribution in CreateRunner(schema).Contributions)
        {
            await using var connection = await _OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = string.Join(Environment.NewLine, contribution.Steps.Select(s => s.Sql));
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private void _AddPilotFeatures(IServiceCollection services, string schema)
    {
        var connectionString = ConnectionString;

        services.AddHeadlessSequences(setup =>
            setup.UseSqlite(options =>
            {
                options.ConnectionString = connectionString;
                options.Schema = schema;
            })
        );
        services.AddHeadlessIdempotency(setup =>
        {
            setup.UseSqlite(connectionString);
            setup.ConfigureStorage(options => options.Schema = schema);
        });
        services.AddHeadlessCoordination(setup =>
        {
            setup.UseSqlite(options => options.ConnectionString = connectionString);
            setup.ConfigureStorage(options => options.Schema = schema);
        });
    }

    private static string _InSchema(string schema)
    {
        // The schema's objects are the names it prefixes; '_' is a LIKE wildcard, so the prefix is matched exactly.
        return $"substr(name, 1, {schema.Length + 1}) = '{schema}_'";
    }

    private static string? _FindSqliteShell()
    {
        return Environment
            .GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "sqlite3.exe" : "sqlite3"))
            .FirstOrDefault(File.Exists);
    }

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> _ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = await _OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> _OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private sealed class ForeignCreator(
        string connectionString,
        SqliteConnection connection,
        SqliteTransaction transaction
    ) : IForeignCreator
    {
        private bool _committed;

        /// <summary>
        /// SQLite reports no waiters, so this answers the condition the suite waits for: whether the foreign
        /// transaction holds the write lock every other writer, the runner included, must wait for.
        /// </summary>
        public async Task<bool> IsBlockingAnotherSessionAsync(CancellationToken cancellationToken)
        {
            // Zero would mean no timeout; one second bounds how long the probe waits on the held lock.
            var impatient = new SqliteConnectionStringBuilder(connectionString) { DefaultTimeout = 1 }.ToString();
            await using var probe = new SqliteConnection(impatient);
            await probe.OpenAsync(cancellationToken);

            try
            {
                await using var attempt = await probe.BeginTransactionAsync(cancellationToken);
                await attempt.RollbackAsync(cancellationToken);

                return false;
            }
            catch (SqliteException e) when (e.SqliteErrorCode is 5)
            {
                return true;
            }
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
