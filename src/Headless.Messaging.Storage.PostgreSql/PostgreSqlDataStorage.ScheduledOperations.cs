// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Primitives;
using Npgsql;
using NpgsqlTypes;

namespace Headless.Messaging.Storage.PostgreSql;

#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // Buffered row reads cannot add blocking I/O.
#pragma warning disable CA2100 // SQL structure is assembled only from provider-owned table names and fixed filter fragments; values remain parameterized.

internal sealed partial class PostgreSqlDataStorage
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
            where += " AND \"Name\"=@Name";
        }

        if (query.Lane is not null)
        {
            where += " AND \"IntentType\"=@IntentType";
        }

        if (query.DueFrom is not null)
        {
            where += " AND \"ExpiresAt\">=@DueFrom";
        }

        if (query.DueTo is not null)
        {
            where += " AND \"ExpiresAt\"<=@DueTo";
        }

        if (query.StorageIds is { Count: > 0 })
        {
            where += " AND \"Id\"=ANY(@StorageIds)";
        }

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var countCmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {_publishedTable} WHERE {where};",
            connection
        );
        _AddScheduledQueryParameters(countCmd, query);
        var total = (long)(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);

        await using var command = new NpgsqlCommand(
            $"""
            SELECT "Id","MessageId","Name","IntentType","ExpiresAt","LockedUntil","Owner","InlineAttempts",COALESCE("LockedUntil" > statement_timestamp(), FALSE)
            FROM {_publishedTable}
            WHERE {where}
            ORDER BY "ExpiresAt" ASC,"Id" ASC
            LIMIT @PageSize OFFSET @Offset;
            """,
            connection
        );
        _AddScheduledQueryParameters(command, query);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        command.Parameters.AddWithValue("@Offset", checked(page * pageSize));

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
    ) => _ExecuteScheduledOperationAsync(MessagingOperationType.Revoke, request, cancellationToken);

    public ValueTask<ScheduledDeliveryOperationResult> DispatchNowAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteScheduledOperationAsync(MessagingOperationType.DispatchNow, request, cancellationToken);

    private async ValueTask<ScheduledDeliveryOperationResult> _ExecuteScheduledOperationAsync(
        MessagingOperationType operationType,
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate();
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _LockPostgreSqlOperationIdAsync(connection, transaction, request.OperationId, cancellationToken)
            .ConfigureAwait(false);

        var prior = await _ReadScheduledReceiptAsync(connection, transaction, request.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (prior is not null)
        {
            var replay = _ReplayOrConflict(prior, operationType, request);
            if (replay.Outcome is InboxOperationOutcome.OperationConflict)
            {
                await _WriteScheduledAuditAsync(connection, transaction, replay, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return replay;
        }

        var now = await _ReadPostgreSqlNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var row = await _ReadScheduledOperationRowAsync(connection, transaction, request.StorageId, cancellationToken)
            .ConfigureAwait(false);

        var outcome = MessagingOperationEvaluator.Evaluate(operationType, request.ExpectedDueAt, row?.State);

        if (outcome is InboxOperationOutcome.Applied && row is not null)
        {
            switch (operationType)
            {
                case MessagingOperationType.Revoke:
                    await _ExecutePostgreSqlScheduledRevokeAsync(connection, transaction, request, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case MessagingOperationType.DispatchNow:
                    await _ExecutePostgreSqlScheduledDispatchNowAsync(
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

        await _WriteScheduledReceiptAndAuditAsync(connection, transaction, result, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (outcome is InboxOperationOutcome.Applied && row is not null)
        {
            MessagingMetrics.RecordScheduledOperation(operationType, row.Lane, outcome, "PostgreSql");
        }

        return result;
    }

    private void _AddScheduledQueryParameters(NpgsqlCommand command, ScheduledDeliveryQuery query)
    {
        command.Parameters.AddWithValue("@Version", messagingOptions.Value.Version);
        if (!string.IsNullOrWhiteSpace(query.MessageName))
        {
            command.Parameters.AddWithValue("@Name", query.MessageName);
        }

        if (query.Lane is { } lane)
        {
            command.Parameters.AddWithValue("@IntentType", (short)lane);
        }

        if (query.DueFrom is { } dueFrom)
        {
            command.Parameters.Add(new NpgsqlParameter("@DueFrom", NpgsqlDbType.TimestampTz) { Value = dueFrom });
        }

        if (query.DueTo is { } dueTo)
        {
            command.Parameters.Add(new NpgsqlParameter("@DueTo", NpgsqlDbType.TimestampTz) { Value = dueTo });
        }

        if (query.StorageIds is { Count: > 0 } storageIds)
        {
            var idsArray = storageIds as Guid[] ?? [.. storageIds];
            command.Parameters.Add(new NpgsqlParameter("@StorageIds", idsArray));
        }
    }

    private async ValueTask<ScheduledDeliveryOperationResult?> _ReadScheduledReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            $"SELECT \"TargetKind\",\"OperationType\",\"Outcome\",\"StorageId\",\"ExpectedDueAt\",\"MessageName\",\"MessageId\",\"Lane\",\"Actor\",\"Reason\",\"CreatedAt\" FROM {InboxReceiptsTable} WHERE \"OperationId\"=@OperationId FOR UPDATE;",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@OperationId", operationId);
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

    private sealed record ScheduledOperationRow(
        ScheduledDeliveryOperationState State,
        string MessageName,
        string MessageId,
        MessageLane Lane
    );

    private async ValueTask<ScheduledOperationRow?> _ReadScheduledOperationRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            $"SELECT \"Version\",\"StatusName\",\"InlineAttempts\",\"Retries\",\"NextRetryAt\",COALESCE(\"LockedUntil\" > statement_timestamp(), FALSE),\"ExpiresAt\",\"Name\",\"MessageId\",\"IntentType\" FROM {_publishedTable} WHERE \"Id\"=@Id FOR UPDATE;",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@Id", storageId);
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
        var dueAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6);
        var name = reader.GetString(7);
        var messageId = reader.GetString(8);
        var lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(9));

        return new ScheduledOperationRow(
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

    private async ValueTask _ExecutePostgreSqlScheduledRevokeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            DELETE FROM {_publishedTable}
            WHERE "Id"=@StorageId AND "Version"=@Version AND "ExpiresAt"=@ExpectedDueAt
              AND {_ScheduledEligibilityPredicate};
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@StorageId", request.StorageId);
        command.Parameters.AddWithValue("@Version", messagingOptions.Value.Version);
        command.Parameters.Add(
            new NpgsqlParameter("@ExpectedDueAt", NpgsqlDbType.TimestampTz) { Value = request.ExpectedDueAt }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _ExecutePostgreSqlScheduledDispatchNowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ScheduledDeliveryOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            UPDATE {_publishedTable}
            SET "StatusName"='Delayed',
                "ExpiresAt"=@Now,
                "LockedUntil"=NULL,
                "Owner"=NULL
            WHERE "Id"=@StorageId AND "Version"=@Version AND "ExpiresAt"=@ExpectedDueAt
              AND (NOT (COALESCE("LockedUntil" > statement_timestamp(), FALSE)))
              AND {_ScheduledEligibilityPredicate};
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@StorageId", request.StorageId);
        command.Parameters.AddWithValue("@Version", messagingOptions.Value.Version);
        command.Parameters.Add(
            new NpgsqlParameter("@ExpectedDueAt", NpgsqlDbType.TimestampTz) { Value = request.ExpectedDueAt }
        );
        command.Parameters.Add(new NpgsqlParameter("@Now", NpgsqlDbType.TimestampTz) { Value = now });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WriteScheduledReceiptAndAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ScheduledDeliveryOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {InboxReceiptsTable}("OperationId","TargetKind","OperationType","Outcome","Actor","Reason","ExpectedDueAt","StorageId","MessageName","MessageId","Lane","CreatedAt")
            VALUES (@OperationId,'ScheduledDelivery',@OperationType,@Outcome,@Actor,@Reason,@ExpectedDueAt,@StorageId,@MessageName,@MessageId,@Lane,@CreatedAt);
            INSERT INTO {InboxAuditTable}("AuditId","OperationId","TargetKind","OperationType","Actor","Reason","Outcome","CreatedAt")
            VALUES (@AuditId,@OperationId,'ScheduledDelivery',@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@AuditId", guidGenerator.Create());
        command.Parameters.AddWithValue("@OperationId", result.OperationId);
        command.Parameters.AddWithValue("@OperationType", result.OperationType.ToString());
        command.Parameters.AddWithValue("@Outcome", result.Outcome.ToString());
        command.Parameters.AddWithValue("@Actor", result.Actor);
        command.Parameters.AddWithValue("@Reason", result.Reason);
        command.Parameters.Add(
            new NpgsqlParameter("@ExpectedDueAt", NpgsqlDbType.TimestampTz) { Value = result.ExpectedDueAt }
        );
        command.Parameters.Add(new NpgsqlParameter("@StorageId", NpgsqlDbType.Uuid) { Value = result.StorageId });
        command.Parameters.Add(
            new NpgsqlParameter("@MessageName", NpgsqlDbType.Varchar)
            {
                Value = (object?)result.MessageName ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@MessageId", NpgsqlDbType.Varchar)
            {
                Value = (object?)result.MessageId ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@Lane", NpgsqlDbType.Varchar)
            {
                Value = (object?)result.Lane?.ToString() ?? DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@CreatedAt", NpgsqlDbType.TimestampTz) { Value = result.CreatedAt }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WriteScheduledAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ScheduledDeliveryOperationResult result,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {InboxAuditTable}(\"AuditId\",\"OperationId\",\"TargetKind\",\"OperationType\",\"Actor\",\"Reason\",\"Outcome\",\"CreatedAt\") VALUES (@AuditId,@OperationId,'ScheduledDelivery',@OperationType,@Actor,@Reason,@Outcome,statement_timestamp());",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@AuditId", guidGenerator.Create());
        command.Parameters.AddWithValue("@OperationId", result.OperationId);
        command.Parameters.AddWithValue("@OperationType", result.OperationType.ToString());
        command.Parameters.AddWithValue("@Actor", result.Actor);
        command.Parameters.AddWithValue("@Reason", result.Reason);
        command.Parameters.AddWithValue("@Outcome", result.Outcome.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

#pragma warning restore CA2100, CA1849, VSTHRD103, AsyncFixer02, MA0042
