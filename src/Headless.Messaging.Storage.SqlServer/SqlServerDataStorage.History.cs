// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;

namespace Headless.Messaging.Storage.SqlServer;

#pragma warning disable CA2100 // SQL identifiers are provider-owned; all values are parameters.

internal sealed partial class SqlServerDataStorage
{
    public async ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand("SELECT CONVERT(datetimeoffset(7), SYSUTCDATETIME());", connection);
        var now = (DateTimeOffset)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return InboxHistoryRetentionCutoffs.Create(now, messagingOptions.Value);
    }

    public async ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(batchSize);
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(
            $"""
            ;WITH candidates AS (
                SELECT TOP (@BatchSize) * FROM {InboxAuditTable}
                WHERE ([OperationType]=N'Cleanup' AND [CreatedAt]<=@Cleanup) OR ([OperationType]<>N'Cleanup' AND [CreatedAt]<=@Operator)
                ORDER BY [CreatedAt],[AuditId]
            ) DELETE FROM candidates;
            """,
            connection
        );
        command.Parameters.Add(new SqlParameter("@Cleanup", cutoffs.CleanupAudit));
        command.Parameters.Add(new SqlParameter("@Operator", cutoffs.OperatorAudit));
        command.Parameters.Add(new SqlParameter("@BatchSize", batchSize));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(batchSize);
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<Guid>();
        // Discover without update locks: mutations acquire the operation lock before touching a receipt.
        await using (
            var command = new SqlCommand(
                $"""
                SELECT TOP (@BatchSize) r.[OperationId] FROM {InboxReceiptsTable} r
                WHERE ((r.[OperationType]=N'Cleanup' AND r.[CreatedAt]<=@Cleanup) OR (r.[OperationType]<>N'Cleanup' AND r.[CreatedAt]<=@Operator))
                  AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a.[OperationId]=r.[OperationId])
                ORDER BY r.[CreatedAt],r.[OperationId];
                """,
                connection
            )
        )
        {
            command.Parameters.Add(new SqlParameter("@Cleanup", cutoffs.CleanupReceipt));
            command.Parameters.Add(new SqlParameter("@Operator", cutoffs.OperatorReceipt));
            command.Parameters.Add(new SqlParameter("@BatchSize", batchSize));
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
            await using var transaction = (SqlTransaction)
                await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await _LockSqlServerOperationIdAsync(connection, transaction, operationId, cancellationToken)
                .ConfigureAwait(false);
            await using var command = new SqlCommand(
                $"""
                DELETE r FROM {InboxReceiptsTable} r WHERE r.[OperationId]=@OperationId
                  AND ((r.[OperationType]=N'Cleanup' AND r.[CreatedAt]<=@Cleanup) OR (r.[OperationType]<>N'Cleanup' AND r.[CreatedAt]<=@Operator))
                  AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a.[OperationId]=r.[OperationId]);
                """,
                connection,
                transaction
            );
            command.Parameters.Add(new SqlParameter("@OperationId", operationId));
            command.Parameters.Add(new SqlParameter("@Cleanup", cutoffs.CleanupReceipt));
            command.Parameters.Add(new SqlParameter("@Operator", cutoffs.OperatorReceipt));
            var count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            deleted += count;
        }
        return deleted;
    }
}
