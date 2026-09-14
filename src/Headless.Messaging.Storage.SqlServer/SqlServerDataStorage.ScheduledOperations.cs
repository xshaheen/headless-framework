// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Primitives;
using Microsoft.Data.SqlClient;

namespace Headless.Messaging.Storage.SqlServer;

#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // Buffered row reads cannot add blocking I/O.
#pragma warning disable CA2100 // SQL structure is assembled only from provider-owned table names and fixed filter fragments; values remain parameterized.

internal sealed partial class SqlServerDataStorage
{
    public async ValueTask<IndexPage<ScheduledDeliveryView>> QueryAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    )
    {
        authorization.Validate();
        query.Validate();
        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var where = _ScheduledPendingPredicate;

        if (!string.IsNullOrWhiteSpace(query.MessageName))
        {
            where += " AND [Name]=@Name";
        }

        if (query.Lane is not null)
        {
            where += " AND [IntentType]=@IntentType";
        }

        if (query.DueFrom is not null)
        {
            where += " AND [ExpiresAt]>=@DueFrom";
        }

        if (query.DueTo is not null)
        {
            where += " AND [ExpiresAt]<=@DueTo";
        }

        if (query.StorageIds is { Count: > 0 })
        {
            where += " AND [Id] IN (SELECT [Id] FROM @Ids)";
        }

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var countCmd = new SqlCommand(
            $"SELECT COUNT_BIG(*) FROM {_publishedTable} WHERE {where};",
            connection
        );
        _AddScheduledQueryParameters(countCmd, query);
        var total = (long)(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);

        await using var command = new SqlCommand(
            $"""
            SELECT [Id],[MessageId],[Name],[IntentType],[ExpiresAt],[LockedUntil],[Owner],[InlineAttempts],CAST(CASE WHEN [LockedUntil] > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit)
            FROM {_publishedTable}
            WHERE {where}
            ORDER BY [ExpiresAt] ASC,[Id] ASC
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
            """,
            connection
        );
        _AddScheduledQueryParameters(command, query);
        command.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = pageSize });
        command.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = checked(page * pageSize) });

        var items = new List<ScheduledDeliveryView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(3));
            items.Add(
                new ScheduledDeliveryView(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    lane,
                    reader.GetFieldValue<DateTimeOffset>(4),
                    "Pending",
                    reader.GetBoolean(8),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetInt32(7)
                )
            );
        }

        return new IndexPage<ScheduledDeliveryView>(items, page, pageSize, checked((int)total));
    }

    public ValueTask<ScheduledDeliveryOperationResult> RevokeAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteSqlServerScheduledOperationAsync(MessagingOperationType.Revoke, request, cancellationToken);

    public ValueTask<ScheduledDeliveryOperationResult> DispatchNowAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteSqlServerScheduledOperationAsync(MessagingOperationType.DispatchNow, request, cancellationToken);

    private async ValueTask<ScheduledDeliveryOperationResult> _ExecuteSqlServerScheduledOperationAsync(
        MessagingOperationType operationType,
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate();
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transactionBase = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        var transaction = (SqlTransaction)transactionBase;

        await _LockSqlServerOperationIdAsync(connection, transaction, request.OperationId, cancellationToken)
            .ConfigureAwait(false);

        var prior = await _ReadSqlServerScheduledReceiptAsync(
                connection,
                transaction,
                request.OperationId,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (prior is not null)
        {
            var replay = _ReplayOrConflict(prior, operationType, request);
            if (replay.Outcome is InboxOperationOutcome.OperationConflict)
            {
                await _WriteSqlServerScheduledAuditAsync(connection, transaction, replay, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return replay;
        }

        var now = await _ReadSqlServerNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var row = await _ReadSqlServerScheduledOperationRowAsync(
                connection,
                transaction,
                request.StorageId,
                cancellationToken
            )
            .ConfigureAwait(false);

        var outcome = InboxOperationEvaluator.Evaluate(operationType, request.ExpectedDueAt, row?.State);

        if (outcome is InboxOperationOutcome.Applied && row is not null)
        {
            switch (operationType)
            {
                case MessagingOperationType.Revoke:
                    await _ExecuteSqlServerScheduledRevokeAsync(connection, transaction, request, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case MessagingOperationType.DispatchNow:
                    await _ExecuteSqlServerScheduledDispatchNowAsync(
                            connection,
                            transaction,
                            request,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
            }
        }

        var result = new ScheduledDeliveryOperationResult(
            request.OperationId,
            operationType,
            outcome,
            request.StorageId,
            request.ExpectedDueAt,
            row?.MessageName,
            row?.MessageId,
            row?.Lane,
            request.Actor,
            request.Reason,
            now
        );

        await _WriteSqlServerScheduledReceiptAndAuditAsync(connection, transaction, result, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (outcome is InboxOperationOutcome.Applied && row is not null)
        {
            MessagingMetrics.RecordScheduledOperation(operationType, row.Lane, outcome, "SqlServer");
        }

        return result;
    }

    private void _AddScheduledQueryParameters(SqlCommand command, ScheduledDeliveryQuery query)
    {
        command.Parameters.Add(
            new SqlParameter("@Version", SqlDbType.NVarChar, 20) { Value = messagingOptions.Value.Version }
        );

        if (!string.IsNullOrWhiteSpace(query.MessageName))
        {
            command.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = query.MessageName });
        }

        if (query.Lane is { } lane)
        {
            command.Parameters.Add(new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = (short)lane });
        }

        if (query.DueFrom is { } dueFrom)
        {
            command.Parameters.Add(
                new SqlParameter("@DueFrom", SqlDbType.DateTimeOffset) { Value = dueFrom, Scale = 7 }
            );
        }

        if (query.DueTo is { } dueTo)
        {
            command.Parameters.Add(new SqlParameter("@DueTo", SqlDbType.DateTimeOffset) { Value = dueTo, Scale = 7 });
        }

        if (query.StorageIds is { Count: > 0 } storageIds)
        {
            var idsList = storageIds as IReadOnlyList<Guid> ?? storageIds.ToArray();
            command.Parameters.Add(_BuildIdListTvpParameter(idsList));
        }
    }

    private async ValueTask<ScheduledDeliveryOperationResult?> _ReadSqlServerScheduledReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            $"SELECT [TargetKind],[OperationType],[Outcome],[StorageId],[ExpectedDueAt],[MessageName],[MessageId],[Lane],[Actor],[Reason],[CreatedAt] FROM {InboxReceiptsTable} WITH (UPDLOCK,HOLDLOCK) WHERE [OperationId]=@OperationId;",
            connection,
            transaction
        );
        command.Parameters.Add(new SqlParameter("@OperationId", SqlDbType.UniqueIdentifier) { Value = operationId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var opType = Enum.Parse<MessagingOperationType>(reader.GetString(1));
        var outcome = Enum.Parse<InboxOperationOutcome>(reader.GetString(2));
        var storageId = reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3);
        var expectedDueAt = reader.IsDBNull(4) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(4);
        var messageName = reader.IsDBNull(5) ? null : reader.GetString(5);
        var messageId = reader.IsDBNull(6) ? null : reader.GetString(6);
        var lane = reader.IsDBNull(7) ? (MessageLane?)null : Enum.Parse<MessageLane>(reader.GetString(7));
        var actor = reader.GetString(8);
        var reason = reader.GetString(9);
        var createdAt = reader.GetFieldValue<DateTimeOffset>(10);

        return new ScheduledDeliveryOperationResult(
            operationId,
            opType,
            outcome,
            storageId,
            expectedDueAt,
            messageName,
            messageId,
            lane,
            actor,
            reason,
            createdAt
        );
    }

    private static ScheduledDeliveryOperationResult _ReplayOrConflict(
        ScheduledDeliveryOperationResult prior,
        MessagingOperationType operationType,
        ScheduledDeliveryOperationRequest request
    )
    {
        var matches =
            prior.OperationType == operationType
            && prior.StorageId == request.StorageId
            && prior.ExpectedDueAt == request.ExpectedDueAt
            && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, request.Reason, StringComparison.Ordinal);

        return matches
            ? prior with
            {
                IsReplay = true,
            }
            : new ScheduledDeliveryOperationResult(
                request.OperationId,
                operationType,
                InboxOperationOutcome.OperationConflict,
                request.StorageId,
                request.ExpectedDueAt,
                prior.MessageName,
                prior.MessageId,
                prior.Lane,
                request.Actor,
                request.Reason,
                prior.CreatedAt,
                IsReplay: true
            );
    }

    private sealed record SqlServerScheduledOperationRow(
        ScheduledDeliveryOperationState State,
        string MessageName,
        string MessageId,
        MessageLane Lane
    );

    private async ValueTask<SqlServerScheduledOperationRow?> _ReadSqlServerScheduledOperationRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            $"SELECT [Version],[StatusName],[InlineAttempts],[Retries],[NextRetryAt],CAST(CASE WHEN [LockedUntil] > SYSUTCDATETIME() THEN 1 ELSE 0 END AS bit),[ExpiresAt],[Name],[MessageId],[IntentType] FROM {_publishedTable} WITH (UPDLOCK,HOLDLOCK) WHERE [Id]=@Id;",
            connection,
            transaction
        );
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = storageId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var messageVersion = reader.GetString(0);
        var statusName = Enum.Parse<StatusName>(reader.GetString(1));
        var inlineAttempts = reader.GetInt32(2);
        var retries = reader.GetInt32(3);
        var nextRetryAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4);
        var hasLiveLease = reader.GetBoolean(5);
        var dueAt = reader.IsDBNull(6) ? DateTimeOffset.MinValue : reader.GetFieldValue<DateTimeOffset>(6);
        var name = reader.GetString(7);
        var messageId = reader.GetString(8);
        var lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(9));

        return new SqlServerScheduledOperationRow(
            new ScheduledDeliveryOperationState(
                statusName,
                inlineAttempts,
                retries,
                nextRetryAt,
                hasLiveLease,
                messagingOptions.Value.Version,
                messageVersion,
                dueAt
            ),
            name,
            messageId,
            lane
        );
    }

    private async ValueTask _ExecuteSqlServerScheduledRevokeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            DELETE FROM {_publishedTable}
            WHERE [Id]=@StorageId AND [Version]=@Version AND [ExpiresAt]=@ExpectedDueAt
              AND {_ScheduledEligibilityPredicate};
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(
            new SqlParameter("@StorageId", SqlDbType.UniqueIdentifier) { Value = request.StorageId }
        );
        command.Parameters.Add(
            new SqlParameter("@Version", SqlDbType.NVarChar, 20) { Value = messagingOptions.Value.Version }
        );
        command.Parameters.Add(
            new SqlParameter("@ExpectedDueAt", SqlDbType.DateTimeOffset) { Value = request.ExpectedDueAt, Scale = 7 }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _ExecuteSqlServerScheduledDispatchNowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ScheduledDeliveryOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            UPDATE {_publishedTable}
            SET [StatusName]='Delayed',
                [ExpiresAt]=@Now,
                [LockedUntil]=NULL,
                [Owner]=NULL
            WHERE [Id]=@StorageId AND [Version]=@Version AND [ExpiresAt]=@ExpectedDueAt
              AND (NOT (CASE WHEN [LockedUntil] > SYSUTCDATETIME() THEN 1 ELSE 0 END = 1))
              AND {_ScheduledEligibilityPredicate};
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(
            new SqlParameter("@StorageId", SqlDbType.UniqueIdentifier) { Value = request.StorageId }
        );
        command.Parameters.Add(
            new SqlParameter("@Version", SqlDbType.NVarChar, 20) { Value = messagingOptions.Value.Version }
        );
        command.Parameters.Add(
            new SqlParameter("@ExpectedDueAt", SqlDbType.DateTimeOffset) { Value = request.ExpectedDueAt, Scale = 7 }
        );
        command.Parameters.Add(new SqlParameter("@Now", SqlDbType.DateTimeOffset) { Value = now, Scale = 7 });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WriteSqlServerScheduledReceiptAndAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ScheduledDeliveryOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {InboxReceiptsTable}([OperationId],[TargetKind],[OperationType],[Outcome],[Actor],[Reason],[ExpectedDueAt],[StorageId],[MessageName],[MessageId],[Lane],[CreatedAt])
            VALUES (@OperationId,'ScheduledDelivery',@OperationType,@Outcome,@Actor,@Reason,@ExpectedDueAt,@StorageId,@MessageName,@MessageId,@Lane,@CreatedAt);
            INSERT INTO {InboxAuditTable}([AuditId],[OperationId],[TargetKind],[OperationType],[Actor],[Reason],[Outcome],[CreatedAt])
            VALUES (@AuditId,@OperationId,'ScheduledDelivery',@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(
            new SqlParameter("@AuditId", SqlDbType.UniqueIdentifier) { Value = guidGenerator.Create() }
        );
        command.Parameters.Add(
            new SqlParameter("@OperationId", SqlDbType.UniqueIdentifier) { Value = result.OperationId }
        );
        command.Parameters.Add(
            new SqlParameter("@OperationType", SqlDbType.VarChar, 50) { Value = result.OperationType.ToString() }
        );
        command.Parameters.Add(
            new SqlParameter("@Outcome", SqlDbType.VarChar, 50) { Value = result.Outcome.ToString() }
        );
        command.Parameters.Add(new SqlParameter("@Actor", SqlDbType.NVarChar, 200) { Value = result.Actor });
        command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 1000) { Value = result.Reason });
        command.Parameters.Add(
            new SqlParameter("@ExpectedDueAt", SqlDbType.DateTimeOffset) { Value = result.ExpectedDueAt, Scale = 7 }
        );
        command.Parameters.Add(new SqlParameter("@StorageId", SqlDbType.UniqueIdentifier) { Value = result.StorageId });
        command.Parameters.Add(
            new SqlParameter("@MessageName", SqlDbType.NVarChar, 200)
            {
                Value = (object?)result.MessageName ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@MessageId", SqlDbType.NVarChar, 200)
            {
                Value = (object?)result.MessageId ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@Lane", SqlDbType.VarChar, 50)
            {
                Value = (object?)result.Lane?.ToString() ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@CreatedAt", SqlDbType.DateTimeOffset) { Value = result.CreatedAt, Scale = 7 }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WriteSqlServerScheduledAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ScheduledDeliveryOperationResult result,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            $"INSERT INTO {InboxAuditTable}([AuditId],[OperationId],[TargetKind],[OperationType],[Actor],[Reason],[Outcome],[CreatedAt]) VALUES (@AuditId,@OperationId,'ScheduledDelivery',@OperationType,@Actor,@Reason,@Outcome,SYSDATETIMEOFFSET());",
            connection,
            transaction
        );
        command.Parameters.Add(
            new SqlParameter("@AuditId", SqlDbType.UniqueIdentifier) { Value = guidGenerator.Create() }
        );
        command.Parameters.Add(
            new SqlParameter("@OperationId", SqlDbType.UniqueIdentifier) { Value = result.OperationId }
        );
        command.Parameters.Add(
            new SqlParameter("@OperationType", SqlDbType.VarChar, 50) { Value = result.OperationType.ToString() }
        );
        command.Parameters.Add(new SqlParameter("@Actor", SqlDbType.NVarChar, 200) { Value = result.Actor });
        command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 1000) { Value = result.Reason });
        command.Parameters.Add(
            new SqlParameter("@Outcome", SqlDbType.VarChar, 50) { Value = result.Outcome.ToString() }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

#pragma warning restore CA2100, CA1849, VSTHRD103, AsyncFixer02, MA0042
