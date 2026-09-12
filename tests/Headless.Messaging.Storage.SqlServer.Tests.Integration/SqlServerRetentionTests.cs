// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerRetentionTests(SqlServerTestFixture fixture) : TestBase
{
    private readonly string _schema = $"retention_{Guid.NewGuid():N}";
    private string _table = null!;
    private IOptions<SqlServerOptions> _sqlServerOptions = null!;
    private IStorageInitializer _initializer = null!;
    private IDataStorage _storage = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        _sqlServerOptions = Options.Create(
            new SqlServerOptions { ConnectionString = fixture.ConnectionString, Schema = _schema }
        );
        _initializer = new SqlServerStorageInitializer(
            NullLogger<SqlServerStorageInitializer>.Instance,
            _sqlServerOptions,
            messagingOptions
        );
        _table = _initializer.GetReceivedTableName();
        _storage = new SqlServerDataStorage(
            messagingOptions,
            _sqlServerOptions,
            _initializer,
            new JsonUtf8Serializer(messagingOptions),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<SqlServerDataStorage>.Instance
        );
        await _initializer.InitializeAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"""
                DROP TABLE IF EXISTS [{_schema}].InboxAudit;
                DROP TABLE IF EXISTS [{_schema}].InboxOperationReceipts;
                DROP TABLE IF EXISTS [{_schema}].SchemaState;
                DROP TABLE IF EXISTS [{_schema}].Published;
                DROP TABLE IF EXISTS [{_schema}].Received;
                DROP TYPE IF EXISTS [{_schema}].HeadlessMessagingIdList;
                DROP TYPE IF EXISTS [{_schema}].HeadlessMessagingOwnerList;
                DROP TYPE IF EXISTS [{_schema}].HeadlessMessagingPoisonMessageList;
                DROP SCHEMA IF EXISTS [{_schema}];
                """,
                cancellationToken: AbortToken
            )
        );
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_use_retention_index_for_sparse_expiry_cleanup()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $$"""
                DROP INDEX IF EXISTS [IX_{{_schema}}_Received_InboxRetention] ON {{_table}};
                WITH numbers AS (
                    SELECT TOP (20000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                    FROM sys.all_objects a CROSS JOIN sys.all_objects b
                ), seed AS (SELECT i,CONVERT(uniqueidentifier,CONVERT(binary(16),i)) AS id FROM numbers)
                INSERT INTO {{_table}} (Id,Version,Name,Content,Retries,Added,StatusName,MessageId,IntentType,
                    IsInboxRecord,GenerationIncarnationId,LifecycleId,ContractIdentity,ContractVersion,ConsumerIdentity,InboxKeyHash,EffectiveExpiresAt,ExpiresAt)
                SELECT id,N'v1',N'retention.plan',N'{}',0,SYSDATETIMEOFFSET(),N'Succeeded',CONVERT(nvarchar(200),i),0,
                    CASE WHEN i%2=0 THEN 1 ELSE 0 END,id,id,N'retention.plan',N'v1',N'retention.plan',HASHBYTES('SHA2_256',CONVERT(varchar(20),i)),
                    DATEADD(day,CASE WHEN i<=10 THEN -1 ELSE 1 END,SYSDATETIMEOFFSET()),
                    DATEADD(day,CASE WHEN i<=10 THEN -1 ELSE 1 END,SYSDATETIMEOFFSET())
                FROM seed;
                UPDATE STATISTICS {{_table}} WITH FULLSCAN;
                """,
                cancellationToken: AbortToken
            )
        );

        (await _storage!.DeleteExpiresAsync(_initializer.GetReceivedTableName(), DateTimeOffset.UtcNow, 3, AbortToken))
            .Should()
            .Be(3);
        // Replay the exact executed batch with actual-plan reporting; an outer transaction rolls the replay back.
        var sql = await connection.QueryFirstAsync<string>(
            new CommandDefinition(
                $$"""
                SELECT TOP (1) t.text
                FROM sys.dm_exec_query_stats s
                CROSS APPLY sys.dm_exec_sql_text(s.sql_handle) t
                WHERE t.text LIKE '%DECLARE @Candidates TABLE%' AND t.text LIKE '%retention_expired%'
                  AND t.text NOT LIKE '%sys.dm_exec_query_stats%' AND CHARINDEX(@Table,t.text)>0
                ORDER BY s.last_execution_time DESC;
                """,
                new { Table = _table },
                cancellationToken: AbortToken
            )
        );
        Logger.LogInformation(
            "Database engine: {Engine}",
            await connection.QuerySingleAsync<string>(
                new CommandDefinition("SELECT @@VERSION", cancellationToken: AbortToken)
            )
        );
        // The plan cache prefixes parameter declarations; SqlCommand supplies those separately.
        sql = sql[sql.IndexOf("SET NOCOUNT ON;", StringComparison.Ordinal)..];
        var baseline = await _ReadCleanupPlanAsync(connection, sql);
        // Existing schemas must acquire the missing index, and initialization must remain repeatable.
        await _initializer.InitializeAsync(AbortToken);
        await _initializer.InitializeAsync(AbortToken);
        var plan = await _ReadCleanupPlanAsync(connection, sql);
        Logger.LogInformation("Baseline cleanup plan: {Plan}", baseline);
        Logger.LogInformation("Indexed cleanup plan: {Plan}", plan);
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        plan.Descendants(ns + "RelOp")
            .Where(node => (string?)node.Attribute("PhysicalOp") == "Index Seek")
            .SelectMany(node => node.Descendants(ns + "Object"))
            .Should()
            .Contain(node => (string?)node.Attribute("Index") == $"[IX_{_schema}_Received_InboxRetention]");
        var baselineRows = _CandidateRowsRead(baseline);
        var indexedRows = _CandidateRowsRead(plan);
        Logger.LogInformation(
            "Cleanup candidate rows read: baseline={Baseline}, indexed={Indexed}",
            baselineRows,
            indexedRows
        );
        baselineRows.Should().BeGreaterThan(10000);
        indexedRows.Should().BeLessThan(100);
        indexedRows.Should().BeLessThan(baselineRows / 10);
        (await _storage.DeleteExpiresAsync(_initializer.GetReceivedTableName(), DateTimeOffset.UtcNow, 20, AbortToken))
            .Should()
            .Be(7);
        (await _storage.DeleteExpiresAsync(_initializer.GetReceivedTableName(), DateTimeOffset.UtcNow, 20, AbortToken))
            .Should()
            .Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_skip_locked_cleanup_candidates_and_preserve_batch_order(bool inbox)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"""
                INSERT INTO {_table} (Id,Version,Name,Retries,Added,StatusName,MessageId,IntentType,IsInboxRecord,
                    GenerationIncarnationId,LifecycleId,ContractIdentity,ContractVersion,ConsumerIdentity,InboxKeyHash,EffectiveExpiresAt,ExpiresAt)
                SELECT CONVERT(uniqueidentifier,CONVERT(binary(16),i)),N'v1',N'cleanup.locked',0,SYSDATETIMEOFFSET(),N'Succeeded',CONVERT(nvarchar(200),i),0,@Inbox,
                    CONVERT(uniqueidentifier,CONVERT(binary(16),i)),CONVERT(uniqueidentifier,CONVERT(binary(16),i)),
                    N'cleanup.locked',N'v1',N'cleanup.locked',HASHBYTES('SHA2_256',CONVERT(varchar(20),i)),
                    DATEADD(day,i-4,SYSDATETIMEOFFSET()),DATEADD(day,i-4,SYSDATETIMEOFFSET())
                FROM (VALUES (1),(2),(3)) seed(i);
                """,
                new { Inbox = inbox },
                cancellationToken: AbortToken
            )
        );

        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken))
        {
            // Updating both expiry keys holds row locks on whichever index this cleanup branch uses.
            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"""
                    UPDATE {_table} WITH (ROWLOCK)
                    SET ExpiresAt=DATEADD(second,-1,ExpiresAt),EffectiveExpiresAt=DATEADD(second,-1,EffectiveExpiresAt)
                    WHERE MessageId=N'1';
                    """,
                    transaction: transaction,
                    cancellationToken: AbortToken
                )
            );
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            (await _storage.DeleteExpiresAsync(_table, DateTimeOffset.UtcNow, 1, timeout.Token)).Should().Be(1);
            var remaining = await connection.QueryAsync<string>(
                new CommandDefinition(
                    $"SELECT MessageId FROM {_table} ORDER BY EffectiveExpiresAt,Id;",
                    transaction: transaction,
                    cancellationToken: AbortToken
                )
            );
            remaining.Should().Equal("1", "3");
            await transaction.RollbackAsync(AbortToken);
        }

        (await _storage.DeleteExpiresAsync(_table, DateTimeOffset.UtcNow, 1, AbortToken)).Should().Be(1);
        (
            await connection.QuerySingleAsync<string>(
                new CommandDefinition($"SELECT MessageId FROM {_table};", cancellationToken: AbortToken)
            )
        )
            .Should()
            .Be("3");
        (await _storage.DeleteExpiresAsync(_table, DateTimeOffset.UtcNow, 1, AbortToken)).Should().Be(1);
        var counts = await connection.QuerySingleAsync<(int Receipts, int Audits)>(
            new CommandDefinition(
                $"""
                SELECT (SELECT COUNT(*) FROM [{_schema}].InboxOperationReceipts),
                    (SELECT COUNT(*) FROM [{_schema}].InboxAudit);
                """,
                cancellationToken: AbortToken
            )
        );
        counts.Should().Be(inbox ? (3, 3) : (0, 0));
    }

    private static long _CandidateRowsRead(XDocument plan)
    {
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        return plan.Descendants(ns + "StmtSimple")
            .Where(node =>
                ((string?)node.Attribute("StatementText"))
                    ?.TrimStart()
                    .StartsWith("INSERT INTO @Candidates", StringComparison.Ordinal) == true
            )
            .SelectMany(node => node.Descendants(ns + "RunTimeCountersPerThread"))
            .Sum(node => (long?)node.Attribute("ActualRowsRead") ?? 0);
    }

    private static async Task<XDocument> _ReadCleanupPlanAsync(SqlConnection connection, string sql)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(AbortToken);
        await connection.ExecuteAsync(
            new CommandDefinition("SET STATISTICS XML ON;", transaction: transaction, cancellationToken: AbortToken)
        );
        XDocument? plan = null;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("@timeout", DateTimeOffset.UtcNow);
            command.Parameters.AddWithValue("@batchCount", 3);
            await using var reader = await command.ExecuteReaderAsync(AbortToken);
            do
            {
                while (await reader.ReadAsync(AbortToken))
                {
                    if (
                        reader.FieldCount == 1
                        && reader.GetValue(0) is string xml
                        && xml.Contains("<ShowPlanXML", StringComparison.Ordinal)
                        && xml.Contains("INSERT INTO @Candidates", StringComparison.Ordinal)
                    )
                    {
                        plan = XDocument.Parse(xml);
                    }
                }
            } while (await reader.NextResultAsync(AbortToken));
        }
        await connection.ExecuteAsync(
            new CommandDefinition("SET STATISTICS XML OFF;", transaction: transaction, cancellationToken: AbortToken)
        );
        await transaction.RollbackAsync(AbortToken);
        plan.Should().NotBeNull();
        return plan!;
    }
}
