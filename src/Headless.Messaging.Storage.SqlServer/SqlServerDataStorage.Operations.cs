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
    private string InboxReceiptsTable => $"[{options.Value.Schema}].[InboxOperationReceipts]";

    private string InboxAuditTable => $"[{options.Value.Schema}].[InboxAudit]";

    public async ValueTask<IndexPage<InboxGenerationView>> QueryAsync(
        InboxGenerationQuery query,
        InboxAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    )
    {
        authorization.Validate();
        var page = Math.Max(query.CurrentPage, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var where = "[IsInboxRecord]=1";
        if (query.IncarnationId is not null)
        {
            where += " AND [GenerationIncarnationId]=@IncarnationId";
        }
        if (!string.IsNullOrEmpty(query.ConsumerIdentity))
        {
            where += " AND [ConsumerIdentityOrdinal]=CONVERT(varbinary(400),@ConsumerIdentity)";
        }
        if (query.Lane is not null)
        {
            where += " AND [IntentType]=@IntentType";
        }
        if (query.Status is not null)
        {
            where += " AND [StatusName]=@StatusName";
        }
        if (query.IsOrphaned is not null)
        {
            where += " AND [IsInboxOrphaned]=@IsOrphaned";
        }
        if (query.IsHeld is not null)
        {
            where += " AND [IsHeld]=@IsHeld";
        }

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var count = new SqlCommand($"SELECT COUNT_BIG(*) FROM {_receivedTable} WHERE {where}", connection);
        _AddInboxQueryParameters(count, query);
        var total = (long)(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        await using var command = new SqlCommand(
            $"""
            SELECT [Id],[GenerationIncarnationId],[Generation],[TenantPresent],[TenantId],[MessageId],[IntentType],
                   [ContractIdentity],[ContractVersion],[ConsumerIdentity],[StatusName],[IsCurrentGeneration],
                   [IsInboxOrphaned],[ReplayParentIncarnationId],[ReplayOperationId],[TerminalAt],[EffectiveExpiresAt],
                   [IsHeld],[HeldAt],[HeldBy],[HoldReason]
            FROM {_receivedTable}
            WHERE {where}
            ORDER BY [Added] DESC,[Id]
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;
            """,
            connection
        );
        _AddInboxQueryParameters(command, query);
        command.Parameters.Add(new SqlParameter("@Offset", SqlDbType.BigInt) { Value = (long)page * pageSize });
        command.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = pageSize });
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
    ) => _ExecuteSqlServerInboxOperationAsync(InboxOperationType.Hold, request, cancellationToken);

    public ValueTask<InboxOperationResult> ReleaseHoldAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteSqlServerInboxOperationAsync(InboxOperationType.ReleaseHold, request, cancellationToken);

    public ValueTask<InboxOperationResult> ForceReprocessAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteSqlServerInboxOperationAsync(InboxOperationType.ForceReprocess, request, cancellationToken);

    public ValueTask<InboxOperationResult> PurgeAsync(
        InboxOperationRequest request,
        CancellationToken cancellationToken = default
    ) => _ExecuteSqlServerInboxOperationAsync(InboxOperationType.Purge, request, cancellationToken);

    private async ValueTask<InboxOperationResult> _ExecuteSqlServerInboxOperationAsync(
        InboxOperationType operationType,
        InboxOperationRequest request,
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
        var prior = await _ReadSqlServerInboxReceiptAsync(
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
                await _WriteSqlServerAuditAsync(connection, transaction, replay, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return replay;
        }

        var now = await _ReadSqlServerNowAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        var row = await _ReadSqlServerInboxOperationRowAsync(
                connection,
                transaction,
                request.ExpectedIncarnationId,
                cancellationToken
            )
            .ConfigureAwait(false);
        var outcome = _EvaluateRelationalOperation(operationType, request, row?.Common);
        Guid? childStorageId = null;
        long? childGeneration = null;
        Guid? childIncarnationId = null;
        if (outcome is InboxOperationOutcome.Applied && row is not null)
        {
            switch (operationType)
            {
                case InboxOperationType.Hold:
                    await _ExecuteSqlServerMutationAsync(
                            connection,
                            transaction,
                            $"UPDATE {_receivedTable} SET [IsHeld]=1,[HeldAt]=@Now,[HeldBy]=@Actor,[HoldReason]=@Reason,[HoldOperationId]=@OperationId WHERE [GenerationIncarnationId]=@IncarnationId;",
                            request,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
                case InboxOperationType.ReleaseHold:
                    await _ExecuteSqlServerMutationAsync(
                            connection,
                            transaction,
                            $"UPDATE {_receivedTable} SET [IsHeld]=0,[HeldAt]=NULL,[HeldBy]=NULL,[HoldReason]=NULL,[HoldOperationId]=@OperationId WHERE [GenerationIncarnationId]=@IncarnationId;",
                            request,
                            now,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    break;
                case InboxOperationType.ForceReprocess:
                    childStorageId = guidGenerator.Create();
                    childIncarnationId = guidGenerator.Create();
                    childGeneration = checked(row.Common.Generation + 1);
                    await _CreateSqlServerChildAsync(
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
                    await _ExecuteSqlServerMutationAsync(
                            connection,
                            transaction,
                            $"DELETE FROM {_receivedTable} WHERE [GenerationIncarnationId]=@IncarnationId;",
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
            row?.Common.StorageId,
            childStorageId,
            childGeneration,
            childIncarnationId,
            request.Actor,
            request.Reason,
            now
        );
        await _WriteSqlServerReceiptAndAuditAsync(connection, transaction, result, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _RecordSqlServerInboxOperation(row, operationType, outcome);
        return result;
    }

    private void _RecordSqlServerInboxOperation(
        SqlServerInboxOperationRow? row,
        InboxOperationType operationType,
        InboxOperationOutcome outcome
    )
    {
        if (row is null || outcome is not InboxOperationOutcome.Applied)
        {
            return;
        }
        MessagingMetrics.RecordInbox(
            operationType is InboxOperationType.ForceReprocess ? InboxMetricKind.Replay : InboxMetricKind.Retention,
            row.ConsumerIdentity,
            MessageLaneCompatibility.FromPersistedValue(row.IntentType),
            operationType switch
            {
                InboxOperationType.Hold => InboxMetricOutcome.Held,
                InboxOperationType.ReleaseHold => InboxMetricOutcome.Released,
                InboxOperationType.ForceReprocess => InboxMetricOutcome.Replayed,
                InboxOperationType.Purge => InboxMetricOutcome.Purged,
                _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, message: null),
            },
            messagingOptions.Value.RequiredInboxCapability,
            "SqlServer"
        );
    }

    private static InboxOperationOutcome _EvaluateRelationalOperation(
        InboxOperationType operationType,
        InboxOperationRequest request,
        InboxOperationRow? row
    )
    {
        if (row is null)
        {
            return InboxOperationOutcome.NotFound;
        }
        if (row.Status != request.ExpectedStatus)
        {
            return InboxOperationOutcome.StateConflict;
        }
        if (row.Status is not (StatusName.Succeeded or StatusName.Failed) || row.NextRetryAt is not null)
        {
            return InboxOperationOutcome.Active;
        }
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
                StorageId: null,
                ChildStorageId: null,
                ChildGeneration: null,
                ChildIncarnationId: null,
                request.Actor,
                request.Reason,
                prior.CreatedAt,
                IsReplay: true
            );
    }

    private static void _AddInboxQueryParameters(SqlCommand command, InboxGenerationQuery query)
    {
        command.Parameters.Add(
            new SqlParameter("@IncarnationId", SqlDbType.UniqueIdentifier)
            {
                Value = query.IncarnationId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@ConsumerIdentity", SqlDbType.NVarChar, 200)
            {
                Value = query.ConsumerIdentity ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@IntentType", SqlDbType.SmallInt)
            {
                Value = query.Lane is null
                    ? (object)DBNull.Value
                    : MessageLaneCompatibility.ToPersistedValue(query.Lane.Value),
            }
        );
        command.Parameters.Add(
            new SqlParameter("@StatusName", SqlDbType.NVarChar, 50)
            {
                Value = query.Status?.ToString() ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@IsOrphaned", SqlDbType.Bit) { Value = query.IsOrphaned ?? (object)DBNull.Value }
        );
        command.Parameters.Add(
            new SqlParameter("@IsHeld", SqlDbType.Bit) { Value = query.IsHeld ?? (object)DBNull.Value }
        );
    }

    private static async ValueTask<DateTimeOffset> _ReadSqlServerNowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand("SELECT SYSDATETIMEOFFSET();", connection, transaction);
        return (DateTimeOffset)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    private static async ValueTask _LockSqlServerOperationIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            "DECLARE @Result int; EXEC @Result=sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000; SELECT @Result;",
            connection,
            transaction
        );
        command.Parameters.Add(
            new SqlParameter("@Resource", SqlDbType.NVarChar, 255)
            {
                Value = $"headless.messaging.inbox.operation.{operationId:D}",
            }
        );
        var result = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        if (result < 0)
        {
            throw new TimeoutException("Could not acquire the inbox operation identity lock.");
        }
    }

    private async ValueTask<SqlServerInboxOperationRow?> _ReadSqlServerInboxOperationRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid incarnationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            $"SELECT [Id],[StatusName],[NextRetryAt],[IsHeld],[IsCurrentGeneration],[Generation],[TenantPresent],[TenantId],[MessageId],[IntentType],[ContractIdentity],[ContractVersion],[ConsumerIdentity],[LifecycleId] FROM {_receivedTable} WITH (UPDLOCK,HOLDLOCK) WHERE [IsInboxRecord]=1 AND [GenerationIncarnationId]=@IncarnationId;",
            connection,
            transaction
        );
        command.Parameters.Add(new SqlParameter("@IncarnationId", incarnationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        var common = new InboxOperationRow(
            reader.GetGuid(0),
            Enum.Parse<StatusName>(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetInt64(5)
        );
        return new SqlServerInboxOperationRow(
            common,
            reader.GetBoolean(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetInt16(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetGuid(13)
        );
    }

    private async ValueTask<InboxOperationResult?> _ReadSqlServerInboxReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            $"SELECT [GenerationIncarnationId],[OperationType],[ExpectedStatus],[Actor],[Reason],[Outcome],[StorageId],[ChildStorageId],[ChildGeneration],[ChildIncarnationId],[CreatedAt] FROM {InboxReceiptsTable} WITH (UPDLOCK,HOLDLOCK) WHERE [OperationId]=@OperationId;",
            connection,
            transaction
        );
        command.Parameters.Add(new SqlParameter("@OperationId", operationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
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

    private static async ValueTask _ExecuteSqlServerMutationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        InboxOperationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@IncarnationId", request.ExpectedIncarnationId));
        command.Parameters.Add(new SqlParameter("@OperationId", request.OperationId));
        command.Parameters.Add(new SqlParameter("@Actor", SqlDbType.NVarChar, 200) { Value = request.Actor });
        command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 1000) { Value = request.Reason });
        command.Parameters.Add(new SqlParameter("@Now", SqlDbType.DateTimeOffset) { Value = now });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _CreateSqlServerChildAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        SqlServerInboxOperationRow row,
        InboxOperationRequest request,
        Guid childStorageId,
        Guid childIncarnationId,
        long childGeneration,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var hash = _CreateInboxKeyHash(
            row.TenantPresent,
            row.TenantId,
            row.MessageId,
            row.IntentType,
            row.ContractIdentity,
            row.ContractVersion,
            row.ConsumerIdentity,
            childGeneration,
            row.LifecycleId
        );
        var (graceSeconds, graceNanoseconds) = _SplitLeaseDuration(
            messagingOptions.Value.RetryPolicy.InitialDispatchGrace
        );
        var sql = $"""
            UPDATE {_receivedTable} SET [IsCurrentGeneration]=0 WHERE [GenerationIncarnationId]=@ParentIncarnationId AND [IsCurrentGeneration]=1;
            INSERT INTO {_receivedTable}([Id],[Version],[Name],[Group],[Content],[IntentType],[Retries],[InlineAttempts],[Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[Owner],[StatusName],[MessageId],[ExceptionInfo],[IsInboxRecord],[TenantPresent],[TenantId],[ContractIdentity],[ContractVersion],[ConsumerIdentity],[Generation],[GenerationIncarnationId],[LifecycleId],[AttemptId],[IsInboxOrphaned],[IsCurrentGeneration],[ReplayParentIncarnationId],[ReplayOperationId],[TerminalAt],[EffectiveExpiresAt],[IsHeld],[HeldAt],[HeldBy],[HoldReason],[HoldOperationId],[InboxKeyHash],[InboxRetentionSeconds])
            SELECT @ChildStorageId,[Version],[Name],[Group],[Content],[IntentType],0,0,@Now,NULL,DATEADD(nanosecond,@GraceNanoseconds,DATEADD(second,@GraceSeconds,@Now)),NULL,NULL,N'Scheduled',[MessageId],NULL,1,[TenantPresent],[TenantId],[ContractIdentity],[ContractVersion],[ConsumerIdentity],@ChildGeneration,@ChildIncarnationId,[LifecycleId],NULL,0,1,[GenerationIncarnationId],@OperationId,NULL,NULL,0,NULL,NULL,NULL,NULL,@InboxKeyHash,[InboxRetentionSeconds]
            FROM {_receivedTable} WHERE [GenerationIncarnationId]=@ParentIncarnationId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@ParentIncarnationId", request.ExpectedIncarnationId));
        command.Parameters.Add(new SqlParameter("@ChildStorageId", childStorageId));
        command.Parameters.Add(new SqlParameter("@ChildIncarnationId", childIncarnationId));
        command.Parameters.Add(new SqlParameter("@ChildGeneration", SqlDbType.BigInt) { Value = childGeneration });
        command.Parameters.Add(new SqlParameter("@OperationId", request.OperationId));
        command.Parameters.Add(new SqlParameter("@Now", SqlDbType.DateTimeOffset) { Value = now });
        command.Parameters.Add(new SqlParameter("@GraceSeconds", SqlDbType.Int) { Value = graceSeconds });
        command.Parameters.Add(new SqlParameter("@GraceNanoseconds", SqlDbType.Int) { Value = graceNanoseconds });
        command.Parameters.Add(new SqlParameter("@InboxKeyHash", SqlDbType.Binary, 32) { Value = hash });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WriteSqlServerReceiptAndAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InboxOperationResult result,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            INSERT INTO {InboxReceiptsTable}([OperationId],[GenerationIncarnationId],[OperationType],[ExpectedStatus],[Actor],[Reason],[Outcome],[StorageId],[ChildStorageId],[ChildGeneration],[ChildIncarnationId],[CreatedAt]) VALUES (@OperationId,@IncarnationId,@OperationType,@ExpectedStatus,@Actor,@Reason,@Outcome,@StorageId,@ChildStorageId,@ChildGeneration,@ChildIncarnationId,@CreatedAt);
            INSERT INTO {InboxAuditTable}([AuditId],[OperationId],[GenerationIncarnationId],[OperationType],[Actor],[Reason],[Outcome],[CreatedAt]) VALUES (@AuditId,@OperationId,@IncarnationId,@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@AuditId", guidGenerator.Create()));
        command.Parameters.Add(new SqlParameter("@OperationId", result.OperationId));
        command.Parameters.Add(new SqlParameter("@IncarnationId", result.ExpectedIncarnationId));
        command.Parameters.Add(
            new SqlParameter("@OperationType", SqlDbType.NVarChar, 50) { Value = result.OperationType.ToString() }
        );
        command.Parameters.Add(
            new SqlParameter("@ExpectedStatus", SqlDbType.NVarChar, 50) { Value = result.ExpectedStatus.ToString() }
        );
        command.Parameters.Add(new SqlParameter("@Actor", SqlDbType.NVarChar, 200) { Value = result.Actor });
        command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 1000) { Value = result.Reason });
        command.Parameters.Add(
            new SqlParameter("@Outcome", SqlDbType.NVarChar, 50) { Value = result.Outcome.ToString() }
        );
        command.Parameters.Add(
            new SqlParameter("@StorageId", SqlDbType.UniqueIdentifier)
            {
                Value = result.StorageId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@ChildStorageId", SqlDbType.UniqueIdentifier)
            {
                Value = result.ChildStorageId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@ChildGeneration", SqlDbType.BigInt)
            {
                Value = result.ChildGeneration ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(
            new SqlParameter("@ChildIncarnationId", SqlDbType.UniqueIdentifier)
            {
                Value = result.ChildIncarnationId ?? (object)DBNull.Value,
            }
        );
        command.Parameters.Add(new SqlParameter("@CreatedAt", SqlDbType.DateTimeOffset) { Value = result.CreatedAt });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _WriteSqlServerAuditAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InboxOperationResult result,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(
            $"INSERT INTO {InboxAuditTable}([AuditId],[OperationId],[GenerationIncarnationId],[OperationType],[Actor],[Reason],[Outcome],[CreatedAt]) VALUES (@AuditId,@OperationId,@IncarnationId,@OperationType,@Actor,@Reason,@Outcome,SYSDATETIMEOFFSET());",
            connection,
            transaction
        );
        command.Parameters.Add(new SqlParameter("@AuditId", guidGenerator.Create()));
        command.Parameters.Add(new SqlParameter("@OperationId", result.OperationId));
        command.Parameters.Add(new SqlParameter("@IncarnationId", result.ExpectedIncarnationId));
        command.Parameters.Add(
            new SqlParameter("@OperationType", SqlDbType.NVarChar, 50) { Value = result.OperationType.ToString() }
        );
        command.Parameters.Add(new SqlParameter("@Actor", SqlDbType.NVarChar, 200) { Value = result.Actor });
        command.Parameters.Add(new SqlParameter("@Reason", SqlDbType.NVarChar, 1000) { Value = result.Reason });
        command.Parameters.Add(
            new SqlParameter("@Outcome", SqlDbType.NVarChar, 50) { Value = result.Outcome.ToString() }
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record InboxOperationRow(
        Guid StorageId,
        StatusName Status,
        DateTimeOffset? NextRetryAt,
        bool IsHeld,
        bool IsCurrent,
        long Generation
    );

    private sealed record SqlServerInboxOperationRow(
        InboxOperationRow Common,
        bool TenantPresent,
        string TenantId,
        string MessageId,
        short IntentType,
        string ContractIdentity,
        string ContractVersion,
        string ConsumerIdentity,
        Guid LifecycleId
    );
}

#pragma warning restore CA2100, CA1849, VSTHRD103, AsyncFixer02, MA0042
