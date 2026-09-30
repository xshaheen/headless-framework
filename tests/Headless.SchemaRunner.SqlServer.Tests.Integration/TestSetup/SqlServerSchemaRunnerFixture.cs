// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.RegularExpressions;
using Headless.Coordination;
using Headless.Hosting.Initialization.Schema;
using Headless.Idempotency;
using Headless.Sequences;
using Headless.Sql.SqlServer;
using Headless.Testing.Testcontainers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.TestSetup;

/// <summary>
/// SQL Server fixture for the schema runner suite. Each runner comes from its own service provider registering the
/// real Sequences, Idempotency, and Coordination providers, so one runner stands for one replica. Tests run serially
/// because the measurement and race scenarios observe the whole database.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed partial class SqlServerSchemaRunnerFixture
    : HeadlessSqlServerFixture,
        IAsyncLifetime,
        ICollectionFixture<SqlServerSchemaRunnerFixture>,
        ISchemaRunnerFixture
{
    private const string _Database = "schema_runner_test";

    public ISchemaDialect Dialect => SqlServerSchemaDialect.Instance;

    // Sequences 1, Idempotency 1, Coordination 2.
    public int ExpectedStepCount => 4;

    // Sequences, IdempotencyRecords, three coordination tables, and the history table.
    public int ExpectedTableCount => 6;

    // The runner's sys.schemas guard waits on the uncommitted foreign schema and then sees it committed, so the race
    // resolves without an error; the test still asserts the outcome but not a re-run.
    public bool ForeignCreatorForcesRerun => false;

    private string DatabaseConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = _Database, MaxPoolSize = 20 }.ToString();

    // Re-implemented rather than overridden: the base fixture's InitializeAsync is not virtual, and the database can
    // only be created once its container accepts logins.
    public new async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var master = new SqlConnection(ConnectionString);
        await master.OpenAsync(CancellationToken.None);
        await using var create = new SqlCommand(
            $"IF DB_ID(N'{_Database}') IS NULL CREATE DATABASE [{_Database}];",
            master
        );
        await create.ExecuteNonQueryAsync(CancellationToken.None);
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

    public Task DropSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        return _ExecuteAsync(
            $"""
            DECLARE @drop nvarchar(max) = N'';
            SELECT @drop += N'DROP TABLE [{schema}].' + QUOTENAME(t.name) + N'; '
            FROM sys.tables t WHERE t.schema_id = SCHEMA_ID(N'{schema}');
            SELECT @drop += N'DROP SEQUENCE [{schema}].' + QUOTENAME(s.name) + N'; '
            FROM sys.sequences s WHERE s.schema_id = SCHEMA_ID(N'{schema}');
            EXEC(@drop);
            IF SCHEMA_ID(N'{schema}') IS NOT NULL EXEC(N'DROP SCHEMA [{schema}]');
            """,
            cancellationToken
        );
    }

    public async Task ExecuteScriptWithPlainClientAsync(string script, CancellationToken cancellationToken)
    {
        // sqlcmd's client-side contract: split on lines that hold only GO and send each batch as-is. The container
        // image used on ARM ships no sqlcmd, so this reproduces it without any Headless code on the path.
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in GoSeparatorRegex.Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IForeignCreator> BeginForeignCreatorAsync(string schema, CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // False positive: the returned ForeignCreator owns and disposes the connection.
        var connection = new SqlConnection(DatabaseConnectionString);
#pragma warning restore CA2000
        await connection.OpenAsync(cancellationToken);

        await using var spidCommand = new SqlCommand("SELECT @@SPID;", connection);
        var spid = Convert.ToInt32(
            await spidCommand.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture
        );

        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        // What an EF migration owning the Sequences table would run: the schema, then the table, uncommitted.
        await using var create = new SqlCommand(
            $"""
            EXEC(N'CREATE SCHEMA [{schema}]');
            CREATE TABLE [{schema}].[Sequences] (
                [TenantId] nvarchar(128) NOT NULL,
                [Name] nvarchar(128) NOT NULL,
                [Partition] nvarchar(64) NOT NULL,
                [Value] bigint NOT NULL,
                [CreatedAt] datetime2 NOT NULL,
                [UpdatedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_Sequences] PRIMARY KEY CLUSTERED ([TenantId], [Name], [Partition])
            );
            """,
            connection,
            transaction
        );
        await create.ExecuteNonQueryAsync(cancellationToken);

        return new ForeignCreator(DatabaseConnectionString, connection, transaction, spid);
    }

    public Task TamperChecksumAsync(string schema, string feature, string version, CancellationToken cancellationToken)
    {
        return _ExecuteAsync(
            $"""
            UPDATE [{schema}].[headless_schema_history] SET [Checksum] = REPLICATE('0', 64)
            WHERE [Feature] = N'{feature}' AND [StepVersion] = N'{version}';
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
            DELETE FROM [{schema}].[headless_schema_history]
            WHERE [Feature] = N'{feature}' AND [StepVersion] = N'{version}';
            """,
            cancellationToken
        );
    }

    public Task<int> CountHistoryRowsAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync($"SELECT count(*) FROM [{schema}].[headless_schema_history];", cancellationToken);
    }

    public Task<int> CountTablesAsync(string schema, CancellationToken cancellationToken)
    {
        return _ScalarAsync(
            $"SELECT count(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'{schema}');",
            cancellationToken
        );
    }

    public async Task RunLegacyInitializerProtocolAsync(string schema, CancellationToken cancellationToken)
    {
        // The deleted initializers, one after another as the host ran them: each opened its own connection and sent
        // one batch that took its session applock, created the schema, re-ran its whole idempotent DDL in a
        // transaction, and released the lock.
        foreach (var contribution in CreateRunner(schema).Contributions)
        {
            await using var connection = new SqlConnection(DatabaseConnectionString);
            await connection.OpenAsync(cancellationToken);

            var resource = $"headless_{contribution.Feature.ToLowerInvariant()}_init:{schema}";
            var batch = $"""
                DECLARE @lockResult int;
                EXEC @lockResult = sp_getapplock @Resource = N'{resource}',
                    @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000;
                IF @lockResult < 0 THROW 50000, N'lock', 1;
                BEGIN TRAN;
                IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{schema}') EXEC(N'CREATE SCHEMA [{schema}]');
                {string.Join(Environment.NewLine, contribution.Steps.Select(s => s.Sql))}
                COMMIT TRAN;
                EXEC sp_releaseapplock @Resource = N'{resource}', @LockOwner = N'Session';
                """;

            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private void _AddPilotFeatures(IServiceCollection services, string schema)
    {
        var connectionString = DatabaseConnectionString;

        services.AddHeadlessSequences(setup =>
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = connectionString;
                options.Schema = schema;
            })
        );
        services.AddHeadlessIdempotency(setup =>
        {
            setup.UseSqlServer(connectionString);
            setup.ConfigureStorage(options => options.Schema = schema);
        });
        services.AddHeadlessCoordination(setup =>
        {
            setup.UseSqlServer(options => options.ConnectionString = connectionString);
            setup.ConfigureStorage(options => options.Schema = schema);
        });
    }

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> _ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(
        @"^[ \t]*GO[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex GoSeparatorRegex { get; }

    private sealed class ForeignCreator(
        string connectionString,
        SqlConnection connection,
        SqlTransaction transaction,
        int spid
    ) : IForeignCreator
    {
        private bool _committed;

        public async Task<bool> IsBlockingAnotherSessionAsync(CancellationToken cancellationToken)
        {
            await using var probe = new SqlConnection(connectionString);
            await probe.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.dm_exec_requests WHERE blocking_session_id = @spid) THEN 1 ELSE 0 END;",
                probe
            );
            command.Parameters.AddWithValue("spid", spid);

            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture)
                == 1;
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
