// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Monitoring;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA2100 // SQL text is rendered from dialect output, table names, and fixed fragments; every value is a parameter.

internal sealed partial class RelationalDataStorage
{
    // Retention selects one branch per persisted OperationType so each branch seeks the (OperationType, CreatedAt)
    // index in CreatedAt order and reads at most one batch. A single `<>` or multi-value IN predicate cannot keep that
    // order, so the planner would scan or sort the whole expired backlog on every collector batch. Deriving the
    // branches from the enum keeps a newly added operation type from being silently excluded from retention.
    private static readonly MessagingOperationType[] _HistoryOperationTypes = Enum.GetValues<MessagingOperationType>();

    public async ValueTask<InboxHistoryRetentionCutoffs> GetInboxHistoryRetentionCutoffsAsync(
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _CreateConnection();
        var now = await _ReadNowAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);

        return InboxHistoryRetentionCutoffs.Create(now, Options);
    }

    public async ValueTask<int> DeleteExpiredInboxAuditsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(batchSize);

        // Each branch locks its own expired batch, skipping audits another collector holds; the batches are merged in
        // creation order and trimmed, and the trimmed rows are unlocked when the transaction ends.
        var branches = _HistoryOperationTypes
            .Select(type =>
                _dialect.Render(
                    new SqlLockBatch(
                        _t.Audit,
                        [_t.AuditId, _t.CreatedAt],
                        $"{_t.OperationType}=@Type AND {_t.CreatedAt}<=@Cutoff",
                        [_t.CreatedAt, _t.AuditId],
                        "BatchSize"
                    )
                )
            )
            .ToArray();

        return await SqlAutonomousTransaction
            .RunAsync(
                "messaging.delete_expired_inbox_audits",
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    var candidates = new List<(Guid Id, DateTimeOffset CreatedAt)>();
                    for (var i = 0; i < _HistoryOperationTypes.Length; i++)
                    {
                        var type = _HistoryOperationTypes[i];
                        candidates.AddRange(
                            await RelationalCommand
                                .ExecuteReaderAsync(
                                    connection,
                                    transaction,
                                    branches[i],
                                    CommandTimeoutSeconds,
                                    command =>
                                    {
                                        _dialect.AddParameter(command, "Type", _StatusType, type.ToString());
                                        _dialect.AddParameter(
                                            command,
                                            "Cutoff",
                                            SqlColumnType.Timestamp,
                                            type == MessagingOperationType.Cleanup
                                                ? cutoffs.CleanupAudit
                                                : cutoffs.OperatorAudit
                                        );
                                        _dialect.AddParameter(command, "BatchSize", SqlColumnType.Int32, batchSize);
                                    },
                                    _ReadIdAndInstantAsync,
                                    ct
                                )
                                .ConfigureAwait(false)
                        );
                    }

                    var doomed = candidates
                        .OrderBy(static row => row.CreatedAt)
                        .ThenBy(static row => row.Id)
                        .Take(batchSize)
                        .Select(static row => row.Id)
                        .ToArray();

                    if (doomed.Length == 0)
                    {
                        return 0;
                    }

                    return await _DeleteLockedAsync(connection, transaction, _t.Audit, _t.AuditId, doomed, ct)
                        .ConfigureAwait(false);
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<int> DeleteExpiredInboxReceiptsAsync(
        InboxHistoryRetentionCutoffs cutoffs,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(batchSize);

        // Discover without row locks: a mutation takes the operation lock before it touches a receipt, so each delete
        // below takes it too and checks the receipt's age and references again. The audit-reference check stays
        // inside each branch so a branch's limit counts only deletable receipts.
        var candidates = new List<(Guid Id, DateTimeOffset CreatedAt)>();
        await using (var connection = _CreateConnection())
        {
            foreach (var type in _HistoryOperationTypes)
            {
                candidates.AddRange(
                    await RelationalCommand
                        .ExecuteReaderAsync(
                            connection,
                            transaction: null,
                            $"SELECT r.{_t.OperationId},r.{_t.CreatedAt} FROM {_t.Receipts} r WHERE r.{_t.OperationType}=@Type AND r.{_t.CreatedAt}<=@Cutoff AND NOT EXISTS (SELECT 1 FROM {_t.Audit} a WHERE a.{_t.OperationId}=r.{_t.OperationId}) ORDER BY r.{_t.CreatedAt},r.{_t.OperationId} {_dialect.Limit("BatchSize")};",
                            CommandTimeoutSeconds,
                            command =>
                            {
                                _dialect.AddParameter(command, "Type", _StatusType, type.ToString());
                                _dialect.AddParameter(
                                    command,
                                    "Cutoff",
                                    SqlColumnType.Timestamp,
                                    type == MessagingOperationType.Cleanup
                                        ? cutoffs.CleanupReceipt
                                        : cutoffs.OperatorReceipt
                                );
                                _dialect.AddParameter(command, "BatchSize", SqlColumnType.Int32, batchSize);
                            },
                            _ReadIdAndInstantAsync,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                );
            }
        }

        var deleted = 0;
        foreach (
            var operationId in candidates
                .OrderBy(static row => row.CreatedAt)
                .ThenBy(static row => row.Id)
                .Take(batchSize)
                .Select(static row => row.Id)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            deleted += await SqlAutonomousTransaction
                .RunAsync(
                    "messaging.delete_expired_inbox_receipts",
                    _CreateConnection,
                    async (connection, transaction, ct) =>
                    {
                        await _LockOperationAsync(connection, transaction, operationId, ct).ConfigureAwait(false);

                        // Keyed by the primary key, so the type/cutoff predicate is a residual filter, not a seek.
                        return await RelationalCommand
                            .ExecuteNonQueryAsync(
                                connection,
                                transaction,
                                $"DELETE FROM {_t.Receipts} WHERE {_t.OperationId}=@OperationId AND {_t.CreatedAt}<=CASE WHEN {_t.OperationType}=@CleanupType THEN @Cleanup ELSE @Operator END AND NOT EXISTS (SELECT 1 FROM {_t.Audit} a WHERE a.{_t.OperationId}=@OperationId);",
                                CommandTimeoutSeconds,
                                command =>
                                {
                                    _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, operationId);
                                    _dialect.AddParameter(
                                        command,
                                        "CleanupType",
                                        _StatusType,
                                        nameof(MessagingOperationType.Cleanup)
                                    );
                                    _dialect.AddParameter(
                                        command,
                                        "Cleanup",
                                        SqlColumnType.Timestamp,
                                        cutoffs.CleanupReceipt
                                    );
                                    _dialect.AddParameter(
                                        command,
                                        "Operator",
                                        SqlColumnType.Timestamp,
                                        cutoffs.OperatorReceipt
                                    );
                                },
                                ct
                            )
                            .ConfigureAwait(false);
                    },
                    _timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return deleted;
    }

    private static async Task<List<(Guid Id, DateTimeOffset CreatedAt)>> _ReadIdAndInstantAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<(Guid, DateTimeOffset)>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(
                (
                    reader.GetGuid(0),
                    await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken).ConfigureAwait(false)
                )
            );
        }

        return rows;
    }
}

#pragma warning restore CA2100
