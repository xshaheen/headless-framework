// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlInboxOperationPolicyTests(PostgreSqlTestFixture fixture)
    : InboxOperationPolicyConformanceTests
{
    [Theory]
    [InlineData(-365)]
    [InlineData(365)]
    public async Task should_use_database_history_clock_and_exact_cutoff(int skewDays)
    {
        await using var provider = _CreateProvider(new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(skewDays)));
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var schema = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value.Schema;
        await storage.GetInboxOperationsApi().HoldAsync(_Request(Guid.NewGuid(), StatusName.Succeeded), AbortToken);
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        (await storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken)).Should().Be(0);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var align = new NpgsqlCommand(
            $"""
            UPDATE "{schema}"."inbox_audit" SET "CreatedAt"=@Audit;
            UPDATE "{schema}"."inbox_operation_receipts" SET "CreatedAt"=@Receipt;
            """,
            connection
        );
        align.Parameters.AddWithValue("@Audit", cutoffs.OperatorAudit);
        align.Parameters.AddWithValue("@Receipt", cutoffs.OperatorReceipt.AddSeconds(1));
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
        var schema = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value.Schema;
        await initializer.InitializeAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var count = new NpgsqlCommand(
            "SELECT COUNT(*) FROM pg_indexes WHERE schemaname=@Schema AND indexname IN ('idx_inbox_receipts_type_created','idx_inbox_audit_type_created','idx_inbox_audit_operation');",
            connection
        );
        count.Parameters.AddWithValue("@Schema", schema);
        (await count.ExecuteScalarAsync(AbortToken)).Should().Be(3L);
        await using var drop = new NpgsqlCommand(
            $"""
            DROP INDEX "{schema}"."idx_inbox_receipts_type_created";
            DROP INDEX "{schema}"."idx_inbox_audit_type_created";
            DROP INDEX "{schema}"."idx_inbox_audit_operation";
            """,
            connection
        );
        await drop.ExecuteNonQueryAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken);
        await initializer.InitializeAsync(AbortToken);
        (await count.ExecuteScalarAsync(AbortToken)).Should().Be(3L);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_serialize_receipt_deletion_with_operation_replay(bool conflict)
    {
        await using var provider = _CreateProvider();
        await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        var storage = provider.GetRequiredService<IDataStorage>();
        var schema = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value.Schema;
        var request = _Request(Guid.NewGuid(), StatusName.Succeeded);
        await storage.GetInboxOperationsApi().HoldAsync(request, AbortToken);
        await AgeHistoryAsync(provider, TimeSpan.FromDays(100));
        var cutoffs = await storage.GetInboxHistoryRetentionCutoffsAsync(AbortToken);
        (await storage.DeleteExpiredInboxAuditsAsync(cutoffs, 1, AbortToken)).Should().Be(1);
        await using var blocker = new NpgsqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(AbortToken);
        await using var transaction = await blocker.BeginTransactionAsync(AbortToken);
        await using var operationLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@OperationId::text,0));",
            blocker,
            transaction
        );
        operationLock.Parameters.AddWithValue("@OperationId", request.OperationId);
        await operationLock.ExecuteNonQueryAsync(AbortToken);
        var deletion = storage.DeleteExpiredInboxReceiptsAsync(cutoffs, 1, AbortToken).AsTask();
        var mutation = storage
            .GetInboxOperationsApi()
            .HoldAsync(conflict ? request with { Reason = "conflicting replay" } : request, AbortToken)
            .AsTask();
        try
        {
            await using var observer = new NpgsqlConnection(fixture.ConnectionString);
            await observer.OpenAsync(AbortToken);
            await using var waiting = new NpgsqlCommand(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE @Blocker = ANY(pg_blocking_pids(pid));",
                observer
            );
            waiting.Parameters.AddWithValue("@Blocker", blocker.ProcessID);
            var overlapped = false;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if ((long)(await waiting.ExecuteScalarAsync(AbortToken))! >= 2)
                {
                    overlapped = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
            }
            overlapped
                .Should()
                .BeTrue("receipt deletion and the public operation must both wait on the operation identity lock");
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
        await using var integrity = new NpgsqlCommand(
            $"""
            SELECT COUNT(*) FROM "{schema}"."inbox_audit" a
            LEFT JOIN "{schema}"."inbox_operation_receipts" r ON r."OperationId"=a."OperationId"
            WHERE r."OperationId" IS NULL;
            """,
            blocker
        );
        (await integrity.ExecuteScalarAsync(AbortToken)).Should().Be(0L);
        await using var count = new NpgsqlCommand(
            $"""SELECT COUNT(*) FROM "{schema}"."inbox_operation_receipts";""",
            blocker
        );
        (await count.ExecuteScalarAsync(AbortToken)).Should().Be(result.IsReplay && !conflict ? 0L : 1L);
    }

    protected override async Task AgeHistoryAsync(ServiceProvider provider, TimeSpan age)
    {
        var schema = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value.Schema;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE "{schema}"."inbox_operation_receipts" SET "CreatedAt"="CreatedAt"-@Age;
            UPDATE "{schema}"."inbox_audit" SET "CreatedAt"="CreatedAt"-@Age;
            """,
            connection
        );
        command.Parameters.AddWithValue("@Age", age);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    protected override async Task ExpireGenerationAsync(ServiceProvider provider, Guid storageId)
    {
        var table = provider.GetRequiredService<IStorageInitializer>().GetReceivedTableName();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new NpgsqlCommand(
            $"""UPDATE {table} SET "EffectiveExpiresAt"=clock_timestamp()-INTERVAL '1 second' WHERE "Id"=@Id;""",
            connection
        );
        command.Parameters.AddWithValue("@Id", storageId);
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

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        var table = initializer.GetReceivedTableName();
        await using var expire = new NpgsqlCommand(
            $"""UPDATE {table} SET "LockedUntil"=clock_timestamp()-INTERVAL '1 second', "NextRetryAt"=clock_timestamp()-INTERVAL '1 second' WHERE "Id"=@Id;""",
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
        await using var blocker = new NpgsqlConnection(fixture.ConnectionString);
        await blocker.OpenAsync(AbortToken);
        await using var transaction = await blocker.BeginTransactionAsync(AbortToken);
        await using var rowLock = new NpgsqlCommand(
            $"""SELECT "Id" FROM {table} WHERE "Id"=@Id FOR UPDATE;""",
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
            await using var observer = new NpgsqlConnection(fixture.ConnectionString);
            await observer.OpenAsync(AbortToken);
            await using var waiting = new NpgsqlCommand(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE cardinality(pg_blocking_pids(pid)) > 0 AND POSITION(@Table IN query) > 0;",
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
        setup.UsePostgreSql(options =>
        {
            options.ConnectionString = fixture.ConnectionString;
            options.Schema = $"inbox_policy_{Guid.NewGuid():N}";
        });
    }
}
