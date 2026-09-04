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
    private string InboxReceiptsTable => $"\"{postgreSqlOptions.Value.Schema}\".\"inbox_operation_receipts\"";

    private string InboxAuditTable => $"\"{postgreSqlOptions.Value.Schema}\".\"inbox_audit\"";

    public async ValueTask<IndexPage<InboxGenerationView>> QueryAsync(
        InboxGenerationQuery query,
        InboxAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    )
    {
        authorization.Validate();
        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var where = "\"IsInboxRecord\"";
        if (query.IncarnationId is not null)
            where += " AND \"GenerationIncarnationId\"=@IncarnationId";
        if (!string.IsNullOrEmpty(query.ConsumerIdentity))
            where += " AND \"ConsumerIdentity\"=@ConsumerIdentity";
        if (query.Lane is not null)
            where += " AND \"IntentType\"=@IntentType";
        if (query.Status is not null)
            where += " AND \"StatusName\"=@StatusName";
        if (query.IsOrphaned is not null)
            where += " AND \"IsInboxOrphaned\"=@IsOrphaned";
        if (query.IsHeld is not null)
            where += " AND \"IsHeld\"=@IsHeld";

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var count = new NpgsqlCommand($"SELECT COUNT(*) FROM {_receivedTable} WHERE {where}", connection);
        _AddInboxQueryParameters(count, query);
        var total = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);

        await using var command = new NpgsqlCommand(
            $"""
            SELECT "Id","GenerationIncarnationId","Generation","TenantPresent","TenantId","MessageId","IntentType",
                   "ContractIdentity","ContractVersion","ConsumerIdentity","StatusName","IsCurrentGeneration",
                   "IsInboxOrphaned","ReplayParentIncarnationId","ReplayOperationId","TerminalAt","EffectiveExpiresAt",
                   "IsHeld","HeldAt","HeldBy","HoldReason"
            FROM {_receivedTable}
            WHERE {where}
            ORDER BY "Added" DESC,"Id"
            OFFSET @Offset LIMIT @Limit;
            """,
            connection
        );
        _AddInboxQueryParameters(command, query);
        command.Parameters.AddWithValue("@Offset", (long)page * pageSize);
        command.Parameters.AddWithValue("@Limit", pageSize);
        var items = new List<InboxGenerationView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(
                new InboxGenerationView(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt64(2),
                    reader.GetBoolean(3) ? reader.GetString(4) : null,
                    reader.GetString(5),
                    MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(6)),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    Enum.Parse<StatusName>(reader.GetString(10)),
                    reader.GetBoolean(11),
                    reader.GetBoolean(12),
                    reader.IsDBNull(13) ? null : reader.GetGuid(13),
                    reader.IsDBNull(14) ? null : reader.GetGuid(14),
                    reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
                    reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                    reader.GetBoolean(17),
                    reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
                    reader.IsDBNull(19) ? null : reader.GetString(19),
                    reader.IsDBNull(20) ? null : reader.GetString(20)
                )
            );
        }

        return new IndexPage<InboxGenerationView>(items, page, pageSize, checked((int)total));
    }

    public ValueTask<InboxOperationResult> HoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(InboxOperationType.Hold, request, cancellationToken);

    public ValueTask<InboxOperationResult> ReleaseHoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(InboxOperationType.ReleaseHold, request, cancellationToken);

    public ValueTask<InboxOperationResult> ForceReprocessAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(InboxOperationType.ForceReprocess, request, cancellationToken);

    public ValueTask<InboxOperationResult> PurgeAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteInboxOperationAsync(InboxOperationType.Purge, request, cancellationToken);

    private async ValueTask<InboxOperationResult> _ExecuteInboxOperationAsync(
        InboxOperationType operationType,
        InboxOperationRequest request,
        CancellationToken cancellationToken
    )
    {
        request.Validate();
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _LockPostgreSqlOperationIdAsync(connection, transaction, request.OperationId, cancellationToken)
            .ConfigureAwait(false);

        var prior = await _ReadInboxReceiptAsync(connection, transaction, request.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (prior is not null)
        {
            var replay = _ReplayOrConflict(prior, operationType, request);
            if (replay.Outcome is InboxOperationOutcome.OperationConflict)
            {
                await _WritePostgreSqlAuditAsync(connection, transaction, replay, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return replay;
        }

        var now = await _ReadPostgreSqlNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var row = await _ReadInboxOperationRowAsync(
                connection,
                transaction,
                request.ExpectedIncarnationId,
                cancellationToken
            )
            .ConfigureAwait(false);
        var outcome = _EvaluateRelationalOperation(operationType, request, row);
        Guid? childStorageId = null;
        long? childGeneration = null;
        Guid? childIncarnationId = null;

        if (outcome is InboxOperationOutcome.Applied && row is not null)
        {
            switch (operationType)
            {
                case InboxOperationType.Hold:
                    await _ExecutePostgreSqlMutationAsync(
                            connection,
                            transaction,
                            $"UPDATE {_receivedTable} SET \"IsHeld\"=TRUE,\"HeldAt\"=@Now,\"HeldBy\"=@Actor,\"HoldReason\"=@Reason,\"HoldOperationId\"=@OperationId WHERE \"GenerationIncarnationId\"=@IncarnationId;",
                            request,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
                case InboxOperationType.ReleaseHold:
                    await _ExecutePostgreSqlMutationAsync(
                            connection,
                            transaction,
                            $"UPDATE {_receivedTable} SET \"IsHeld\"=FALSE,\"HeldAt\"=NULL,\"HeldBy\"=NULL,\"HoldReason\"=NULL,\"HoldOperationId\"=@OperationId WHERE \"GenerationIncarnationId\"=@IncarnationId;",
                            request,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
                case InboxOperationType.ForceReprocess:
                    childStorageId = guidGenerator.Create();
                    childIncarnationId = guidGenerator.Create();
                    childGeneration = checked(row.Generation + 1);
                    await _CreatePostgreSqlChildAsync(
                            connection,
                            transaction,
                            row,
                            request,
                            childStorageId.Value,
                            childIncarnationId.Value,
                            childGeneration.Value,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
                case InboxOperationType.Purge:
                    await _ExecutePostgreSqlMutationAsync(
                            connection,
                            transaction,
                            $"DELETE FROM {_receivedTable} WHERE \"GenerationIncarnationId\"=@IncarnationId;",
                            request,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
            }
        }

        var result = new InboxOperationResult(
            request.OperationId,
            operationType,
            outcome,
            request.ExpectedIncarnationId,
            request.ExpectedStatus,
            row?.StorageId,
            childStorageId,
            childGeneration,
            childIncarnationId,
            request.Actor,
            request.Reason,
            now
        );
        await _WritePostgreSqlReceiptAndAuditAsync(connection, transaction, result, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _RecordPostgreSqlInboxOperation(row, operationType, outcome);
        return result;
    }

    private void _RecordPostgreSqlInboxOperation(
        InboxOperationRow? row,
        InboxOperationType operationType,
        InboxOperationOutcome outcome
    )
    {
        if (row is null || outcome is not InboxOperationOutcome.Applied)
            return;
        var kind =
            operationType is InboxOperationType.ForceReprocess ? InboxMetricKind.Replay : InboxMetricKind.Retention;
        var metricOutcome = operationType switch
        {
            InboxOperationType.Hold => InboxMetricOutcome.Held,
            InboxOperationType.ReleaseHold => InboxMetricOutcome.Released,
            InboxOperationType.ForceReprocess => InboxMetricOutcome.Replayed,
            InboxOperationType.Purge => InboxMetricOutcome.Purged,
            _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, message: null),
        };
        MessagingMetrics.RecordInbox(
            kind,
            row.ConsumerIdentity,
            row.Lane,
            metricOutcome,
            messagingOptions.Value.RequiredInboxCapability,
            "PostgreSql"
        );
    }

    private static InboxOperationOutcome _EvaluateRelationalOperation(
        InboxOperationType operationType,
        InboxOperationRequest request,
        InboxOperationRow? row
    )
    {
        if (row is null)
            return InboxOperationOutcome.NotFound;
        if (row.Status != request.ExpectedStatus)
            return InboxOperationOutcome.StateConflict;
        if (row.Status is not (StatusName.Succeeded or StatusName.Failed) || row.NextRetryAt is not null)
            return InboxOperationOutcome.Active;
        return operationType switch
        {
            InboxOperationType.Hold when row.IsHeld => InboxOperationOutcome.StateConflict,
            InboxOperationType.ReleaseHold when !row.IsHeld => InboxOperationOutcome.StateConflict,
            InboxOperationType.ForceReprocess when !row.IsCurrent || row.Generation == long.MaxValue =>
                InboxOperationOutcome.StateConflict,
            InboxOperationType.Purge when row.IsHeld => InboxOperationOutcome.Held,
            _ => InboxOperationOutcome.Applied,
        };
    }

    private static InboxOperationResult _ReplayOrConflict(
        InboxOperationResult prior,
        InboxOperationType operationType,
        InboxOperationRequest request
    )
    {
        var matches =
            prior.OperationType == operationType
            && prior.ExpectedIncarnationId == request.ExpectedIncarnationId
            && prior.ExpectedStatus == request.ExpectedStatus
            && string.Equals(prior.Actor, request.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, request.Reason, StringComparison.Ordinal);
        return matches
            ? prior with
            {
                IsReplay = true,
            }
            : new InboxOperationResult(
                request.OperationId,
                operationType,
                InboxOperationOutcome.OperationConflict,
                request.ExpectedIncarnationId,
                request.ExpectedStatus,
                null,
                null,
                null,
                null,
                request.Actor,
                request.Reason,
                prior.CreatedAt,
                IsReplay: true
            );
    }

    private static void _AddInboxQueryParameters(NpgsqlCommand command, InboxGenerationQuery query)
    {
        command.Parameters.Add(
            new NpgsqlParameter("@IncarnationId", NpgsqlDbType.Uuid)
            {
                Value = query.IncarnationId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@ConsumerIdentity", NpgsqlDbType.Varchar)
            {
                Value = query.ConsumerIdentity ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@IntentType", NpgsqlDbType.Smallint)
            {
                Value = query.Lane is null
                    ? (object)DBNull.Value
                    : MessageLaneCompatibility.ToPersistedValue(query.Lane.Value),
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@StatusName", NpgsqlDbType.Varchar)
            {
                Value = query.Status?.ToString() ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@IsOrphaned", NpgsqlDbType.Boolean)
            {
                Value = query.IsOrphaned ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@IsHeld", NpgsqlDbType.Boolean) { Value = query.IsHeld ?? (object)DBNull.Value }
        );
    }

    private static async ValueTask<DateTimeOffset> _ReadPostgreSqlNowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand("SELECT transaction_timestamp();", connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException("PostgreSQL did not return a transaction timestamp."),
        };
    }

    private static async ValueTask _LockPostgreSqlOperationIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@OperationId::text, 0));",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@OperationId", operationId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<InboxOperationRow?> _ReadInboxOperationRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid incarnationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            $"SELECT \"Id\",\"StatusName\",\"NextRetryAt\",\"IsHeld\",\"IsCurrentGeneration\",\"Generation\",\"IntentType\",\"ConsumerIdentity\" FROM {_receivedTable} WHERE \"IsInboxRecord\" AND \"GenerationIncarnationId\"=@IncarnationId FOR UPDATE;",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@IncarnationId", incarnationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new InboxOperationRow(
                reader.GetGuid(0),
                Enum.Parse<StatusName>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetInt64(5),
                MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(6)),
                reader.GetString(7)
            )
            : null;
    }

    private async ValueTask<InboxOperationResult?> _ReadInboxReceiptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            $"SELECT \"GenerationIncarnationId\",\"OperationType\",\"ExpectedStatus\",\"Actor\",\"Reason\",\"Outcome\",\"StorageId\",\"ChildStorageId\",\"ChildGeneration\",\"ChildIncarnationId\",\"CreatedAt\" FROM {InboxReceiptsTable} WHERE \"OperationId\"=@OperationId FOR UPDATE;",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@OperationId", operationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new InboxOperationResult(
            operationId,
            Enum.Parse<InboxOperationType>(reader.GetString(1)),
            Enum.Parse<InboxOperationOutcome>(reader.GetString(5)),
            reader.GetGuid(0),
            Enum.Parse<StatusName>(reader.GetString(2)),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(10)
        );
    }

    private static async ValueTask _ExecutePostgreSqlMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        InboxOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@IncarnationId", request.ExpectedIncarnationId);
        command.Parameters.AddWithValue("@OperationId", request.OperationId);
        command.Parameters.AddWithValue("@Actor", request.Actor);
        command.Parameters.AddWithValue("@Reason", request.Reason);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _CreatePostgreSqlChildAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InboxOperationRow row,
        InboxOperationRequest request,
        Guid childStorageId,
        Guid childIncarnationId,
        long childGeneration,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            UPDATE {_receivedTable} SET "IsCurrentGeneration"=FALSE WHERE "GenerationIncarnationId"=@ParentIncarnationId AND "IsCurrentGeneration";
            INSERT INTO {_receivedTable}("Id","Version","Name","Group","Content","IntentType","Retries","InlineAttempts","Added","ExpiresAt","NextRetryAt","LockedUntil","Owner","StatusName","MessageId","ExceptionInfo","IsInboxRecord","TenantPresent","TenantId","ContractIdentity","ContractVersion","ConsumerIdentity","Generation","GenerationIncarnationId","AttemptId","IsInboxOrphaned","IsCurrentGeneration","ReplayParentIncarnationId","ReplayOperationId","TerminalAt","EffectiveExpiresAt","IsHeld","HeldAt","HeldBy","HoldReason","HoldOperationId","InboxRetentionSeconds")
            SELECT @ChildStorageId,"Version","Name","Group","Content","IntentType",0,0,@Now,NULL,@Now + (@GraceSeconds * INTERVAL '1 second'),NULL,NULL,'Scheduled',"MessageId",NULL,TRUE,"TenantPresent","TenantId","ContractIdentity","ContractVersion","ConsumerIdentity",@ChildGeneration,@ChildIncarnationId,NULL,FALSE,TRUE,"GenerationIncarnationId",@OperationId,NULL,NULL,FALSE,NULL,NULL,NULL,NULL,"InboxRetentionSeconds"
            FROM {_receivedTable} WHERE "GenerationIncarnationId"=@ParentIncarnationId;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@ParentIncarnationId", request.ExpectedIncarnationId);
        command.Parameters.AddWithValue("@ChildStorageId", childStorageId);
        command.Parameters.AddWithValue("@ChildIncarnationId", childIncarnationId);
        command.Parameters.AddWithValue("@ChildGeneration", childGeneration);
        command.Parameters.AddWithValue("@OperationId", request.OperationId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue(
            "@GraceSeconds",
            messagingOptions.Value.RetryPolicy.InitialDispatchGrace.TotalSeconds
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WritePostgreSqlReceiptAndAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InboxOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {InboxReceiptsTable}("OperationId","GenerationIncarnationId","OperationType","ExpectedStatus","Actor","Reason","Outcome","StorageId","ChildStorageId","ChildGeneration","ChildIncarnationId","CreatedAt")
            VALUES (@OperationId,@IncarnationId,@OperationType,@ExpectedStatus,@Actor,@Reason,@Outcome,@StorageId,@ChildStorageId,@ChildGeneration,@ChildIncarnationId,@CreatedAt);
            INSERT INTO {InboxAuditTable}("AuditId","OperationId","GenerationIncarnationId","OperationType","Actor","Reason","Outcome","CreatedAt")
            VALUES (@AuditId,@OperationId,@IncarnationId,@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@AuditId", guidGenerator.Create());
        command.Parameters.AddWithValue("@OperationId", result.OperationId);
        command.Parameters.AddWithValue("@IncarnationId", result.ExpectedIncarnationId);
        command.Parameters.AddWithValue("@OperationType", result.OperationType.ToString());
        command.Parameters.AddWithValue("@ExpectedStatus", result.ExpectedStatus.ToString());
        command.Parameters.AddWithValue("@Actor", result.Actor);
        command.Parameters.AddWithValue("@Reason", result.Reason);
        command.Parameters.AddWithValue("@Outcome", result.Outcome.ToString());
        command.Parameters.Add(
            new NpgsqlParameter("@StorageId", NpgsqlDbType.Uuid) { Value = result.StorageId ?? (object)DBNull.Value }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@ChildStorageId", NpgsqlDbType.Uuid)
            {
                Value = result.ChildStorageId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@ChildGeneration", NpgsqlDbType.Bigint)
            {
                Value = result.ChildGeneration ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new NpgsqlParameter("@ChildIncarnationId", NpgsqlDbType.Uuid)
            {
                Value = result.ChildIncarnationId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.AddWithValue("@CreatedAt", result.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WritePostgreSqlAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InboxOperationResult result,
        CancellationToken cancellationToken
    )
    {
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {InboxAuditTable}(\"AuditId\",\"OperationId\",\"GenerationIncarnationId\",\"OperationType\",\"Actor\",\"Reason\",\"Outcome\",\"CreatedAt\") VALUES (@AuditId,@OperationId,@IncarnationId,@OperationType,@Actor,@Reason,@Outcome,transaction_timestamp());",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("@AuditId", guidGenerator.Create());
        command.Parameters.AddWithValue("@OperationId", result.OperationId);
        command.Parameters.AddWithValue("@IncarnationId", result.ExpectedIncarnationId);
        command.Parameters.AddWithValue("@OperationType", result.OperationType.ToString());
        command.Parameters.AddWithValue("@Actor", result.Actor);
        command.Parameters.AddWithValue("@Reason", result.Reason);
        command.Parameters.AddWithValue("@Outcome", result.Outcome.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record InboxOperationRow(
        Guid StorageId,
        StatusName Status,
        DateTimeOffset? NextRetryAt,
        bool IsHeld,
        bool IsCurrent,
        long Generation,
        MessageLane Lane,
        string ConsumerIdentity
    );
}

#pragma warning restore CA2100
#pragma warning restore CA1849, VSTHRD103, AsyncFixer02, MA0042
