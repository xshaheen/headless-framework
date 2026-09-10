// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlRetentionTests(PostgreSqlTestFixture fixture) : TestBase
{
    private readonly string _schema = $"retention_{Guid.NewGuid():N}";
    private string _table = null!;
    private IOptions<PostgreSqlOptions> _postgreSqlOptions = null!;
    private IStorageInitializer _initializer = null!;
    private IDataStorage _storage = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        _postgreSqlOptions = Options.Create(
            new PostgreSqlOptions { ConnectionString = fixture.ConnectionString, Schema = _schema }
        );
        _initializer = new PostgreSqlStorageInitializer(
            NullLogger<PostgreSqlStorageInitializer>.Instance,
            _postgreSqlOptions,
            messagingOptions
        );
        _table = _initializer.GetReceivedTableName();
        _storage = new PostgreSqlDataStorage(
            _postgreSqlOptions,
            messagingOptions,
            _initializer,
            new JsonUtf8Serializer(messagingOptions),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<PostgreSqlDataStorage>.Instance
        );
        await _initializer.InitializeAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition($"""DROP SCHEMA IF EXISTS "{_schema}" CASCADE;""", cancellationToken: AbortToken)
        );
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_use_retention_index_for_sparse_expiry_cleanup()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $$"""
                DROP INDEX IF EXISTS "{{_schema}}".idx_received_inbox_retention;
                INSERT INTO {{_table}} ("Id","Version","Name","Content","Retries","Added","StatusName","MessageId",
                    "IntentType","IsInboxRecord","GenerationIncarnationId","LifecycleId","ContractIdentity","ContractVersion","ConsumerIdentity","EffectiveExpiresAt","ExpiresAt")
                SELECT id,'v1','retention.plan','{}',0,statement_timestamp(),'Succeeded',i::text,
                    0,i%2=0,id,id,'retention.plan','v1','retention.plan',
                    statement_timestamp() + CASE WHEN i<=10 THEN INTERVAL '-1 day' ELSE INTERVAL '1 day' END,
                    statement_timestamp() + CASE WHEN i<=10 THEN INTERVAL '-1 day' ELSE INTERVAL '1 day' END
                FROM (SELECT i,gen_random_uuid() id FROM generate_series(1,20000) i) seed;
                ANALYZE {{_table}};
                """,
                cancellationToken: AbortToken
            )
        );

        // Existing schemas must acquire the missing index, and initialization must remain repeatable.
        await _initializer!.InitializeAsync(AbortToken);
        await _initializer.InitializeAsync(AbortToken);

        // Capture the command executed by the provider so the plan assertion cannot drift from its SQL.
        using var capture = new RetentionCommandLogger();
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Debug).AddProvider(capture)
        );
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(fixture.ConnectionString);
        dataSourceBuilder.UseLoggerFactory(loggerFactory);
        await using var dataSource = dataSourceBuilder.Build();
        _postgreSqlOptions!.Value.DataSource = dataSource;
        try
        {
            (
                await _storage!.DeleteExpiresAsync(
                    _initializer!.GetReceivedTableName(),
                    DateTimeOffset.UtcNow,
                    3,
                    AbortToken
                )
            )
                .Should()
                .Be(3);
            var command = capture.Command;
            command.Should().NotBeNull();
            await using var transaction = await connection.BeginTransactionAsync(AbortToken);
            await using var explain = new NpgsqlCommand(
                "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + command,
                connection,
                transaction
            );
            explain.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = DateTimeOffset.UtcNow });
            explain.Parameters.Add(new NpgsqlParameter<int> { TypedValue = 3 });
            var plan = (string)(await explain.ExecuteScalarAsync(AbortToken))!;
            Logger.LogInformation("PostgreSQL cleanup plan: {Plan}", plan);
            plan.Should().Contain("idx_received_inbox_retention");
            plan.Should().NotContain("\"Node Type\": \"Seq Scan\"");
            await transaction.RollbackAsync(AbortToken);
            (
                await _storage.DeleteExpiresAsync(
                    _initializer.GetReceivedTableName(),
                    DateTimeOffset.UtcNow,
                    20,
                    AbortToken
                )
            )
                .Should()
                .Be(7);
            (
                await _storage.DeleteExpiresAsync(
                    _initializer.GetReceivedTableName(),
                    DateTimeOffset.UtcNow,
                    20,
                    AbortToken
                )
            )
                .Should()
                .Be(0);
            var counts = await connection.QuerySingleAsync<(long Rows, long Receipts, long Audits)>(
                new CommandDefinition(
                    $"""
                    SELECT (SELECT COUNT(*) FROM {_table}),
                        (SELECT COUNT(*) FROM "{_schema}".inbox_operation_receipts),
                        (SELECT COUNT(*) FROM "{_schema}".inbox_audit);
                    """,
                    cancellationToken: AbortToken
                )
            );
            counts.Should().Be((19990L, 5L, 5L));
        }
        finally
        {
            _postgreSqlOptions.Value.DataSource = null;
        }
    }

    private sealed class RetentionCommandLogger : ILoggerProvider, ILogger
    {
        public string? Command { get; private set; }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    if (
                        pair.Key == "CommandText"
                        && pair.Value is string sql
                        && sql.Contains("retention_expired", StringComparison.Ordinal)
                    )
                    {
                        Command = sql;
                    }
                }
            }
        }

        public void Dispose() { }
    }
}
