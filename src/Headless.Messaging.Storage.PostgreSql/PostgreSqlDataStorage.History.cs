// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

#pragma warning disable CA2100 // SQL identifiers are provider-owned; all values are parameters.

internal sealed partial class PostgreSqlDataStorage
{
    // Retention selects one branch per persisted OperationType so each branch seeks the
    // (OperationType, CreatedAt) index in CreatedAt order and reads at most one batch. A single `<>` or
    // multi-value IN predicate cannot keep that order, so the planner would scan or sort the whole expired
    // backlog on every collector batch. Deriving the branches from the enum keeps a newly added operation
    // type from being silently excluded from retention.
    private static readonly InboxOperationType[] _HistoryOperationTypes = Enum.GetValues<InboxOperationType>();

    private static string _HistoryTypeParameter(int index) => "@Type" + index.ToString(CultureInfo.InvariantCulture);

    private static string _HistoryCutoffParameter(InboxOperationType operationType) =>
        operationType == InboxOperationType.Cleanup ? "@Cleanup" : "@Operator";

    private static void _AddHistoryTypeParameters(NpgsqlCommand command)
    {
        for (var i = 0; i < _HistoryOperationTypes.Length; i++)
        {
            command.Parameters.AddWithValue(_HistoryTypeParameter(i), _HistoryOperationTypes[i].ToString());
        }
    }

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
        await using var command = new NpgsqlCommand(_BuildDeleteExpiredInboxAuditsSql(), connection);
        command.CommandTimeout = (int)
            Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
        _AddHistoryTypeParameters(command);
        command.Parameters.AddWithValue("@Cleanup", cutoffs.CleanupAudit);
        command.Parameters.AddWithValue("@Operator", cutoffs.OperatorAudit);
        command.Parameters.AddWithValue("@BatchSize", batchSize);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string _BuildDeleteExpiredInboxAuditsSql()
    {
        // PostgreSQL rejects FOR UPDATE on a UNION leaf, so each branch locks inside its own CTE. SKIP LOCKED
        // still applies before each branch's LIMIT; rows a branch locks beyond the final batch are released
        // when this single-statement transaction ends.
        var branches = string.Join(
            ",\n",
            _HistoryOperationTypes.Select(
                (type, i) =>
                    $"b{i.ToString(CultureInfo.InvariantCulture)} AS (SELECT \"AuditId\",\"CreatedAt\" FROM {InboxAuditTable} WHERE \"OperationType\"={_HistoryTypeParameter(i)} AND \"CreatedAt\"<={_HistoryCutoffParameter(type)} ORDER BY \"CreatedAt\",\"AuditId\" LIMIT @BatchSize FOR UPDATE SKIP LOCKED)"
            )
        );
        var union = string.Join(
            " UNION ALL ",
            _HistoryOperationTypes.Select(
                (_, i) => $"SELECT \"AuditId\",\"CreatedAt\" FROM b{i.ToString(CultureInfo.InvariantCulture)}"
            )
        );

        return $"""
            WITH {branches},
            candidates AS (
                SELECT "AuditId" FROM ({union}) u
                ORDER BY "CreatedAt","AuditId" LIMIT @BatchSize
            )
            DELETE FROM {InboxAuditTable} a USING candidates c WHERE a."AuditId"=c."AuditId";
            """;
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
        await using (var command = new NpgsqlCommand(_BuildExpiredInboxReceiptCandidatesSql(), connection))
        {
            command.CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
            _AddHistoryTypeParameters(command);
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
            // Keyed by the primary key, so the type/cutoff predicate is a residual filter, not a seek.
            await using var command = new NpgsqlCommand(
                $"""
                DELETE FROM {InboxReceiptsTable} r WHERE r."OperationId"=@OperationId
                  AND r."CreatedAt"<=CASE WHEN r."OperationType"=@CleanupType THEN @Cleanup ELSE @Operator END
                  AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a."OperationId"=r."OperationId");
                """,
                connection,
                transaction
            );
            command.CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
            command.Parameters.AddWithValue("@OperationId", operationId);
            command.Parameters.AddWithValue("@CleanupType", nameof(InboxOperationType.Cleanup));
            command.Parameters.AddWithValue("@Cleanup", cutoffs.CleanupReceipt);
            command.Parameters.AddWithValue("@Operator", cutoffs.OperatorReceipt);
            var count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            deleted += count;
        }
        return deleted;
    }

    private string _BuildExpiredInboxReceiptCandidatesSql()
    {
        // The audit-reference check stays inside each branch so a branch's LIMIT counts only deletable receipts.
        var union = string.Join(
            "\n    UNION ALL\n    ",
            _HistoryOperationTypes.Select(
                (type, i) =>
                    $"(SELECT r.\"OperationId\",r.\"CreatedAt\" FROM {InboxReceiptsTable} r WHERE r.\"OperationType\"={_HistoryTypeParameter(i)} AND r.\"CreatedAt\"<={_HistoryCutoffParameter(type)} AND NOT EXISTS (SELECT 1 FROM {InboxAuditTable} a WHERE a.\"OperationId\"=r.\"OperationId\") ORDER BY r.\"CreatedAt\",r.\"OperationId\" LIMIT @BatchSize)"
            )
        );

        return $"""
            SELECT "OperationId" FROM (
                {union}
            ) u
            ORDER BY "CreatedAt","OperationId" LIMIT @BatchSize;
            """;
    }
}
