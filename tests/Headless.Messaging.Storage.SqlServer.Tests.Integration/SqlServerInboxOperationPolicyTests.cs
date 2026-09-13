// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerInboxOperationPolicyTests(SqlServerTestFixture fixture)
    : InboxOperationPolicyConformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_apply_configured_timeout_to_blocked_history_queries(bool receipts)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        provider.GetRequiredService<IOptions<MessagingOptions>>().Value.CommandTimeout = TimeSpan.FromSeconds(1);
        var storage = provider.GetRequiredService<IDataStorage>();
        var schema = provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.Schema;
        await storage.GetInboxOperationsApi().HoldAsync(_Request(Guid.NewGuid(), StatusName.Succeeded), AbortToken);
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        var table = receipts ? "InboxOperationReceipts" : "InboxAudit";
        await using var blocker = new SqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(AbortToken);
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM [{schema}].[{table}] WITH (TABLOCKX,HOLDLOCK);",
            blocker,
            transaction
        );
        await command.ExecuteScalarAsync(AbortToken);
        // The cancellation bound makes a missing command timeout fail without waiting for the driver's default.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var act = async () =>
        {
            if (receipts)
            {
                await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, deadline.Token);
            }
            else
            {
                await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, deadline.Token);
            }
        };
        await act.Should().ThrowAsync<SqlException>().Where(ex => ex.Number == -2);
    }

    [Theory]
    [InlineData(-365)]
    [InlineData(365)]
    public async Task should_use_database_history_clock_and_exact_cutoff(int skewDays)
    {
        await using var provider = _CreateProvider(new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(skewDays)));
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var schema = provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.Schema;
        await storage.GetInboxOperationsApi().HoldAsync(_Request(Guid.NewGuid(), StatusName.Succeeded), AbortToken);
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var align = new SqlCommand(
            $"""
            UPDATE [{schema}].[InboxAudit] SET [CreatedAt]=@Audit;
            UPDATE [{schema}].[InboxOperationReceipts] SET [CreatedAt]=@Receipt;
            """,
            connection
        );
        align.Parameters.Add(new SqlParameter("@Audit", cutoffs.OperatorAudit));
        align.Parameters.Add(new SqlParameter("@Receipt", cutoffs.OperatorReceipt.AddSeconds(1)));
        await align.ExecuteNonQueryAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        align.Parameters["@Receipt"].Value = cutoffs.OperatorReceipt;
        await align.ExecuteNonQueryAsync(AbortToken);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_initialize_and_repair_history_indexes_on_reentry()
    {
        await using var provider = _CreateProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        var schema = provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.Schema;
        await initializer.InitializeAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var count = new SqlCommand(
            "SELECT COUNT(*) FROM sys.indexes WHERE name IN (@Receipts,@Audits,@Operation);",
            connection
        );
        count.Parameters.Add(new SqlParameter("@Receipts", $"IX_{schema}_InboxReceipts_Type_CreatedAt"));
        count.Parameters.Add(new SqlParameter("@Audits", $"IX_{schema}_InboxAudit_Type_CreatedAt"));
        count.Parameters.Add(new SqlParameter("@Operation", $"IX_{schema}_InboxAudit_Operation"));
        (await count.ExecuteScalarAsync(AbortToken)).Should().Be(3);
        await using var drop = new SqlCommand(
            $"""
            DROP INDEX [IX_{schema}_InboxReceipts_Type_CreatedAt] ON [{schema}].[InboxOperationReceipts];
            DROP INDEX [IX_{schema}_InboxAudit_Type_CreatedAt] ON [{schema}].[InboxAudit];
            DROP INDEX [IX_{schema}_InboxAudit_Operation] ON [{schema}].[InboxAudit];
            """,
            connection
        );
        await drop.ExecuteNonQueryAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken);
        (await count.ExecuteScalarAsync(AbortToken)).Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_serialize_receipt_deletion_with_operation_replay(bool conflict)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var schema = provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.Schema;
        var request = _Request(Guid.NewGuid(), StatusName.Succeeded);
        await storage.GetInboxOperationsApi().HoldAsync(request, AbortToken);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(100));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        await using var blocker = new SqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(AbortToken);
        await using var operationLock = new SqlCommand(
            "DECLARE @Result int; EXEC @Result=sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000; SELECT @Result;",
            blocker,
            transaction
        );
        operationLock.Parameters.Add(
            new SqlParameter("@Resource", $"headless.messaging.inbox.operation.{request.OperationId:D}")
        );
        ((int)(await operationLock.ExecuteScalarAsync(AbortToken))!).Should().BeGreaterThanOrEqualTo(0);
        var deletion = storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken).AsTask();
        var mutation = storage
            .GetInboxOperationsApi()
            .HoldAsync(conflict ? request with { Reason = "conflicting replay" } : request, AbortToken)
            .AsTask();
        try
        {
            await using var observer = new SqlConnection(fixture.ConnectionString);
            await observer.OpenAsync(AbortToken);
            // SQL Server may report a queued application-lock waiter as blocked by the earlier waiter.
            await using var waiting = new SqlCommand(
                """
                WITH blocked AS (
                    SELECT session_id FROM sys.dm_exec_requests WHERE blocking_session_id=@Blocker
                    UNION ALL
                    SELECT r.session_id FROM sys.dm_exec_requests r
                    INNER JOIN blocked b ON r.blocking_session_id=b.session_id
                ) SELECT COUNT(DISTINCT session_id) FROM blocked OPTION (MAXRECURSION 16);
                """,
                observer
            );
            waiting.Parameters.Add(new SqlParameter("@Blocker", blocker.ServerProcessId));
            var overlapped = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if ((int)(await waiting.ExecuteScalarAsync(AbortToken))! >= 2)
                {
                    overlapped = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
            }
            await using var diagnostic = new SqlCommand(
                """
                SELECT COALESCE(STRING_AGG(CONVERT(nvarchar(max),CONCAT(session_id,':',blocking_session_id,':',wait_type)),N';'),N'no waiters')
                FROM sys.dm_exec_requests WHERE wait_type IS NOT NULL AND database_id=DB_ID();
                """,
                observer
            );
            var graph = (string)(await diagnostic.ExecuteScalarAsync(AbortToken))!;
            Logger.LogInformation(
                "Operation-lock blocker {Blocker}; session:blocker:wait graph {Graph}",
                blocker.ServerProcessId,
                graph
            );
            overlapped
                .Should()
                .BeTrue(
                    $"both operations must wait on operation-lock owner {blocker.ServerProcessId}; observed session:blocker:wait graph {graph}"
                );
        }
        finally
        {
            await transaction.CommitAsync(AbortToken);
            await Task.WhenAll(deletion, mutation);
        }
        var result = await mutation;
        (await deletion).Should().Be(conflict && result.IsReplay ? 0 : 1);
        result
            .Outcome.Should()
            .Be(conflict && result.IsReplay ? InboxOperationOutcome.OperationConflict : InboxOperationOutcome.NotFound);
        await using var integrity = new SqlCommand(
            $"""
            SELECT COUNT(*) FROM [{schema}].[InboxAudit] a
            LEFT JOIN [{schema}].[InboxOperationReceipts] r ON r.[OperationId]=a.[OperationId]
            WHERE r.[OperationId] IS NULL;
            """,
            blocker
        );
        (await integrity.ExecuteScalarAsync(AbortToken)).Should().Be(0);
        await using var count = new SqlCommand($"SELECT COUNT(*) FROM [{schema}].[InboxOperationReceipts];", blocker);
        (await count.ExecuteScalarAsync(AbortToken)).Should().Be(result.IsReplay && !conflict ? 0 : 1);
    }

    protected override async Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age)
    {
        var schema = provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.Schema;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            UPDATE [{schema}].[InboxOperationReceipts] SET [CreatedAt]=DATEADD(second,-@Age,[CreatedAt]);
            UPDATE [{schema}].[InboxAudit] SET [CreatedAt]=DATEADD(second,-@Age,[CreatedAt]);
            """,
            connection
        );
        command.Parameters.Add(new SqlParameter("@Age", (int)age.TotalSeconds));
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    protected override async Task ExpireGenerationAsync(ServiceProvider provider, Guid storageId)
    {
        var table = provider.GetRequiredService<IStorageInitializer>().GetReceivedTableName();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"UPDATE {table} SET [EffectiveExpiresAt]=DATEADD(second,-1,SYSUTCDATETIME()) WHERE [Id]=@Id;",
            connection
        );
        command.Parameters.Add(new SqlParameter("@Id", storageId));
        (await command.ExecuteNonQueryAsync(AbortToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_use_store_clock_for_orphan_operation_claims(MessageLane lane)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(30));
        await using var provider = _CreateProvider(clock);
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.MarkReceivedInboxOrphanedAsync(message, true, AbortToken)).Should().BeTrue();
        var operations = storage.GetInboxOperationsApi();
        var incarnation = message.InboxGeneration!.IncarnationId;
        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Active);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var table = initializer.GetReceivedTableName();
        await using var expire = new SqlCommand(
            $"UPDATE {table} SET [LockedUntil]=DATEADD(second,-1,SYSUTCDATETIME()),[NextRetryAt]=DATEADD(second,-1,SYSUTCDATETIME()) WHERE [Id]=@Id;",
            connection
        );
        expire.Parameters.AddWithValue("@Id", message.StorageId);
        (await expire.ExecuteNonQueryAsync(AbortToken)).Should().Be(1);
        clock.AdjustTime(DateTimeOffset.UtcNow.AddDays(-30));
        (await operations.HoldAsync(_Request(incarnation, StatusName.Scheduled), AbortToken))
            .Outcome.Should()
            .Be(InboxOperationOutcome.Applied);
        // Retry due times use the scheduling clock; only ownership decisions were under skew.
        clock.AdjustTime(DateTimeOffset.UtcNow);
        var recovered = (await storage.GetReceivedInboxOrphansOfNeedRetryAsync(lane, AbortToken))
            .Should()
            .ContainSingle()
            .Which;
        (await storage.ConfirmReceivedInboxRoutableAsync(recovered, AbortToken)).Should().BeTrue();
        (await storage.ChangeReceiveStateAsync(recovered, StatusName.Succeeded, cancellationToken: AbortToken))
            .Should()
            .BeTrue();
        var rows = await operations.QueryAsync(
            new InboxGenerationQuery { IncarnationId = incarnation },
            _Authorization(),
            AbortToken
        );
        rows.Items.Should().ContainSingle().Which.IsHeld.Should().BeTrue();
    }

    [Theory]
    [InlineData(MessageLane.Bus)]
    [InlineData(MessageLane.Queue)]
    public async Task should_serialize_blocked_recovery_claim_and_orphan_purge(MessageLane lane)
    {
        await using var provider = _CreateProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var message = await _AdmitAsync(storage, lane);
        await _LeaseAsync(storage, message);
        (await storage.DeferReceivedInboxOrphanAsync(message, AbortToken)).Should().BeTrue();
        var table = initializer.GetReceivedTableName();
        await using var blocker = new SqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync(AbortToken);
        await using var rowLock = new SqlCommand(
            $"SELECT [Id] FROM {table} WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@Id;",
            blocker,
            transaction
        );
        rowLock.Parameters.AddWithValue("@Id", message.StorageId);
        await rowLock.ExecuteScalarAsync(AbortToken);

        var claim = storage
            .LeaseReceiveAndReserveAttemptAsync(message, TimeSpan.FromMinutes(5), message.InlineAttempts, AbortToken)
            .AsTask();
        var purge = storage
            .GetInboxOperationsApi()
            .PurgeAsync(_Request(message.InboxGeneration!.IncarnationId, StatusName.Scheduled), AbortToken)
            .AsTask();
        try
        {
            await using var observer = new SqlConnection(fixture.ConnectionString);
            await observer.OpenAsync(AbortToken);
            await using var waiting = new SqlCommand(
                "SELECT COUNT(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.blocking_session_id > 0 AND CHARINDEX(@Table,t.text) > 0;",
                observer
            );
            waiting.Parameters.AddWithValue("@Table", table);
            var blocked = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (
                    Convert.ToInt32(
                        await waiting.ExecuteScalarAsync(AbortToken),
                        System.Globalization.CultureInfo.InvariantCulture
                    ) >= 2
                )
                {
                    blocked = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
            }
            blocked.Should().BeTrue("both operations must overlap while waiting for the locked generation");
        }
        finally
        {
            await transaction.CommitAsync(AbortToken);
            await Task.WhenAll(claim, purge);
        }
        await _AssertRecoveryPurgeAsync(storage, message, claim, purge);
    }

    protected override void ConfigureStorage(MessagingSetupBuilder setup)
    {
        setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
        setup.UseSqlServer(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.Schema = $"inbox_policy_{Guid.NewGuid():N}";
        });
    }
}
