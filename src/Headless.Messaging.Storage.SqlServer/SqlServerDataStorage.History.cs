// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Checks;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;

namespace Headless.Messaging.Storage.SqlServer;

#pragma warning disable CA2100 // SQL identifiers are provider-owned; all values are parameters.

internal sealed partial class SqlServerDataStorage
{
    // Retention selects one branch per persisted OperationType so each branch seeks the
    // (OperationType, CreatedAt) index, whose clustered-key locator also serves the id tie-break, and reads
    // at most one batch. A single `<>` or multi-value IN predicate cannot keep CreatedAt order, so the
    // optimizer would scan or sort the whole expired backlog on every collector batch. Deriving the branches
    // from the enum keeps a newly added operation type from being silently excluded from retention.
    private static readonly InboxOperationType[] _HistoryOperationTypes = Enum.GetValues<InboxOperationType>();

    private static string _HistoryTypeParameter(int index) => "@Type" + index.ToString(CultureInfo.InvariantCulture);

    private static string _HistoryCutoffParameter(InboxOperationType operationType) =>
        operationType == InboxOperationType.Cleanup ? "@Cleanup" : "@Operator";

    private static void _AddHistoryTypeParameters(SqlCommand command)
    {
        for (var i = 0; i < _HistoryOperationTypes.Length; i++)
        {
            command.Parameters.Add(
                new SqlParameter(_HistoryTypeParameter(i), SqlDbType.NVarChar, 50)
                {
                    Value = _HistoryOperationTypes[i].ToString(),
                }
            );
        }
    }

    public async ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand("SELECT CONVERT(datetimeoffset(7), SYSUTCDATETIME());", connection);
        command.CommandTimeout = (int)
            Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
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
        await using var command = new SqlCommand(_BuildDeleteExpiredInboxAuditsSql(), connection);
        command.CommandTimeout = (int)
            Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
        _AddHistoryTypeParameters(command);
        command.Parameters.Add(new SqlParameter("@Cleanup", cutoffs.CleanupAudit));
        command.Parameters.Add(new SqlParameter("@Operator", cutoffs.OperatorAudit));
        command.Parameters.Add(new SqlParameter("@BatchSize", batchSize));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string _BuildDeleteExpiredInboxAuditsSql()
    {
        // A CTE over UNION ALL is not updatable, so the union only selects keys and the base table is
        // deleted through a join on its primary key.
        var union = string.Join(
            "\n        UNION ALL\n        ",
            _HistoryOperationTypes.Select(
                (type, i) =>
                    $"SELECT b.[AuditId],b.[CreatedAt] FROM (SELECT TOP (@BatchSize) [AuditId],[CreatedAt] FROM {InboxAuditTable} WHERE [OperationType]={_HistoryTypeParameter(i)} AND [CreatedAt]<={_HistoryCutoffParameter(type)} ORDER BY [CreatedAt],[AuditId]) b"
            )
        );

        return $"""
            DELETE a FROM {InboxAuditTable} a
            INNER JOIN (
                SELECT TOP (@BatchSize) u.[AuditId] FROM (
                    {union}
                ) u
                ORDER BY u.[CreatedAt],u.[AuditId]
            ) c ON a.[AuditId]=c.[AuditId];
            """;
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
        await using (var command = new SqlCommand(_BuildExpiredInboxReceiptCandidatesSql(), connection))
        {
            command.CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
            _AddHistoryTypeParameters(command);
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
            // Keyed by the primary key, so the type/cutoff predicate is a residual filter, not a seek.
            await using var command = new SqlCommand(
                $"""
                DELETE r FROM {InboxReceiptsTable} r WHERE r.[OperationId]=@OperationId
                  AND r.[CreatedAt]<=CASE WHEN r.[OperationType]=@CleanupType THEN @Cleanup ELSE @Operator END
                  AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a.[OperationId]=r.[OperationId]);
                """,
                connection,
                transaction
            );
            command.CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
            command.Parameters.Add(new SqlParameter("@OperationId", operationId));
            command.Parameters.Add(
                new SqlParameter("@CleanupType", SqlDbType.NVarChar, 50) { Value = nameof(InboxOperationType.Cleanup) }
            );
            command.Parameters.Add(new SqlParameter("@Cleanup", cutoffs.CleanupReceipt));
            command.Parameters.Add(new SqlParameter("@Operator", cutoffs.OperatorReceipt));
            var count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            deleted += count;
        }
        return deleted;
    }

    private string _BuildExpiredInboxReceiptCandidatesSql()
    {
        // The audit-reference check stays inside each branch so a branch's TOP counts only deletable receipts.
        var union = string.Join(
            "\n    UNION ALL\n    ",
            _HistoryOperationTypes.Select(
                (type, i) =>
                    $"SELECT b.[OperationId],b.[CreatedAt] FROM (SELECT TOP (@BatchSize) r.[OperationId],r.[CreatedAt] FROM {InboxReceiptsTable} r WHERE r.[OperationType]={_HistoryTypeParameter(i)} AND r.[CreatedAt]<={_HistoryCutoffParameter(type)} AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a.[OperationId]=r.[OperationId]) ORDER BY r.[CreatedAt],r.[OperationId]) b"
            )
        );

        return $"""
            SELECT TOP (@BatchSize) u.[OperationId] FROM (
                {union}
            ) u
            ORDER BY u.[CreatedAt],u.[OperationId];
            """;
    }
}
