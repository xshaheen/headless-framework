// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Persistence;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

#pragma warning disable CA2100 // SQL identifiers are provider-owned; all values are parameters.

internal sealed partial class PostgreSqlDataStorage
{
    public async ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT clock_timestamp();", connection);
        command.CommandTimeout = (int)
            Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
        var now = (DateTime)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return InboxHistoryRetentionCutoffs.Create(new DateTimeOffset(now), messagingOptions.Value);
    }

    public async ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(batchSize);
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            WITH candidates AS (
                SELECT "AuditId" FROM {InboxAuditTable}
                WHERE ("OperationType"='Cleanup' AND "CreatedAt"<=@Cleanup) OR ("OperationType"<>'Cleanup' AND "CreatedAt"<=@Operator)
                ORDER BY "CreatedAt","AuditId" LIMIT @BatchSize FOR UPDATE SKIP LOCKED
            )
            DELETE FROM {InboxAuditTable} a USING candidates c WHERE a."AuditId"=c."AuditId";
            """,
            connection
        );
        command.CommandTimeout = (int)
            Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
        command.Parameters.AddWithValue("@Cleanup", cutoffs.CleanupAudit);
        command.Parameters.AddWithValue("@Operator", cutoffs.OperatorAudit);
        command.Parameters.AddWithValue("@BatchSize", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(batchSize);
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<Guid>();
        // Discover without row locks: mutations acquire the operation lock before touching a receipt.
        await using (
            var command = new NpgsqlCommand(
                $"""
                SELECT r."OperationId" FROM {InboxReceiptsTable} r
                WHERE ((r."OperationType"='Cleanup' AND r."CreatedAt"<=@Cleanup) OR (r."OperationType"<>'Cleanup' AND r."CreatedAt"<=@Operator))
                  AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a."OperationId"=r."OperationId")
                ORDER BY r."CreatedAt",r."OperationId" LIMIT @BatchSize;
                """,
                connection
            )
        )
        {
            command.CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
            command.Parameters.AddWithValue("@Cleanup", cutoffs.CleanupReceipt);
            command.Parameters.AddWithValue("@Operator", cutoffs.OperatorReceipt);
            command.Parameters.AddWithValue("@BatchSize", batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(reader.GetGuid(0));
            }
        }
        var deleted = 0;
        foreach (var operationId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await _LockPostgreSqlOperationIdAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                $"""
                DELETE FROM {InboxReceiptsTable} r WHERE r."OperationId"=@OperationId
                  AND ((r."OperationType"='Cleanup' AND r."CreatedAt"<=@Cleanup) OR (r."OperationType"<>'Cleanup' AND r."CreatedAt"<=@Operator))
                  AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a."OperationId"=r."OperationId");
                """,
                connection,
                transaction
            );
            command.CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
            command.Parameters.AddWithValue("@OperationId", operationId);
            command.Parameters.AddWithValue("@Cleanup", cutoffs.CleanupReceipt);
            command.Parameters.AddWithValue("@Operator", cutoffs.OperatorReceipt);
            var count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            deleted += count;
        }
        return deleted;
    }
}
