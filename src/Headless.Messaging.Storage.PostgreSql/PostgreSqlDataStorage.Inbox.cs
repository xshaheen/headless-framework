// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Npgsql;
using NpgsqlTypes;

#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // Once a row is buffered, these small typed reads cannot add blocking I/O.
namespace Headless.Messaging.Storage.PostgreSql;

internal sealed partial class PostgreSqlDataStorage
{
    private const int _InboxIdentityMaxLength = 200;
    private const int _InboxContractVersionMaxLength = 100;

    private static async Task<(DateTimeOffset LockedUntil, string? Owner, Guid? AttemptId)?> _ReadInboxLeaseAsync(
        DbDataReader reader,
        CancellationToken cancellationToken
    )
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return (
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2)
        );
    }

    private static bool _ApplyInboxLease(
        MediumMessage message,
        (DateTimeOffset LockedUntil, string? Owner, Guid? AttemptId)? storedLease
    )
    {
        if (storedLease is not { } lease)
        {
            return false;
        }

        message.LockedUntil = lease.LockedUntil;
        message.Owner = lease.Owner;
        if (lease.AttemptId is { } attemptId && message.InboxGeneration is { } generation)
        {
            message.InboxAttemptFence = new InboxAttemptFence(
                message.StorageId,
                message.Lane,
                generation.Number,
                generation.IncarnationId,
                attemptId,
                lease.Owner,
                lease.LockedUntil
            );
        }

        return true;
    }

    public async ValueTask<InboxAdmissionResult> AdmitReceivedMessageAsync(
        string name,
        string group,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation = 0,
        TimeSpan? inboxRetention = null,
        CancellationToken cancellationToken = default
    )
    {
        var tenantId = _ValidateInboxAdmission(name, consumerIdentity, contractVersion, message, generation);
        var tenantPresent = tenantId is not null;
        var normalizedTenantId = tenantId ?? string.Empty;
        var storageId = guidGenerator.Create();
        var incarnationId = guidGenerator.Create();
        var content = serializer.Serialize(message.Origin);
        var intentType = MessageLaneCompatibility.ToPersistedValue(message.Lane);
        var retentionSeconds = _ValidateInboxRetention(inboxRetention);

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var insertSql = $"""
            WITH clock AS (SELECT statement_timestamp() AS now)
            INSERT INTO {_receivedTable}(
                "Id","Version","Name","Group","Content","IntentType","Retries","InlineAttempts",
                "Added","ExpiresAt","NextRetryAt","LockedUntil","Owner","StatusName","MessageId","ExceptionInfo",
                "TenantPresent","TenantId","ContractIdentity","ContractVersion","ConsumerIdentity","Generation",
                "GenerationIncarnationId","AttemptId","IsInboxOrphaned","IsCurrentGeneration","IsInboxRecord","InboxRetentionSeconds"
            )
            SELECT @Id,@Version,@Name,@Group,@Content,@IntentType,0,0,
                clock.now,NULL,clock.now + (@InitialDispatchGraceSeconds * INTERVAL '1 second'),NULL,NULL,@StatusName,@MessageId,NULL,
                @TenantPresent,@TenantId,@ContractIdentity,@ContractVersion,@ConsumerIdentity,@Generation,
                @GenerationIncarnationId,NULL,FALSE,TRUE,TRUE,@InboxRetentionSeconds
            FROM clock
            ON CONFLICT ("TenantPresent","TenantId","MessageId","IntentType","ContractIdentity","ContractVersion","ConsumerIdentity","Generation")
            WHERE "IsInboxRecord"
            DO NOTHING
            RETURNING "Id";
            """;

        object[] parameters =
        [
            new NpgsqlParameter("@Id", storageId),
            new NpgsqlParameter("@Version", messagingOptions.Value.Version),
            new NpgsqlParameter("@Name", name),
            new NpgsqlParameter("@Group", NpgsqlDbType.Varchar) { Value = group ?? (object)DBNull.Value },
            new NpgsqlParameter("@Content", content),
            new NpgsqlParameter("@IntentType", NpgsqlDbType.Smallint) { Value = intentType },
            new NpgsqlParameter(
                "@InitialDispatchGraceSeconds",
                messagingOptions.Value.RetryPolicy.InitialDispatchGrace.TotalSeconds
            ),
            new NpgsqlParameter("@StatusName", nameof(StatusName.Scheduled)),
            new NpgsqlParameter("@MessageId", message.Origin.Id),
            new NpgsqlParameter("@TenantPresent", tenantPresent),
            new NpgsqlParameter("@TenantId", normalizedTenantId),
            new NpgsqlParameter("@ContractIdentity", name),
            new NpgsqlParameter("@ContractVersion", contractVersion),
            new NpgsqlParameter("@ConsumerIdentity", consumerIdentity),
            new NpgsqlParameter("@Generation", generation),
            new NpgsqlParameter("@GenerationIncarnationId", incarnationId),
            new NpgsqlParameter("@InboxRetentionSeconds", retentionSeconds),
        ];

        var insertedId = await connection
            .ExecuteReaderAsync<Guid?>(
                insertSql,
                static async (reader, token) =>
                    await reader.ReadAsync(token).ConfigureAwait(false) ? reader.GetGuid(0) : null,
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: parameters,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var stored = await _ReadInboxGenerationAsync(
                connection,
                transaction,
                tenantPresent,
                normalizedTenantId,
                message.Origin.Id,
                intentType,
                name,
                contractVersion,
                consumerIdentity,
                generation,
                cancellationToken
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var disposition = insertedId is not null
            ? InboxAdmissionDisposition.Winner
            : stored.Status switch
            {
                StatusName.Succeeded when stored.Message.NextRetryAt is null =>
                    InboxAdmissionDisposition.SucceededDuplicate,
                StatusName.Failed when stored.Message.NextRetryAt is null =>
                    InboxAdmissionDisposition.TerminalFailedDuplicate,
                _ => InboxAdmissionDisposition.InFlightDuplicate,
            };

        return new InboxAdmissionResult(disposition, stored.Message);
    }

    private static long _ValidateInboxRetention(TimeSpan? retention)
    {
        var value = retention ?? TimeSpan.FromDays(30);
        if (value <= TimeSpan.Zero || value.Ticks % TimeSpan.TicksPerSecond != 0 || value.TotalSeconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                "Inbox retention must be a positive whole-second duration."
            );
        }

        return checked((int)value.TotalSeconds);
    }

    public async ValueTask<bool> MarkReceivedInboxOrphanedAsync(
        MediumMessage message,
        bool orphaned,
        CancellationToken cancellationToken = default
    )
    {
        if (message.InboxAttemptFence is not { } fence || message.InboxGeneration is null)
        {
            return false;
        }

        var sql = $"""
            UPDATE {_receivedTable}
            SET "IsInboxOrphaned"=@IsInboxOrphaned
            WHERE "Id"=@Id
              AND "IntentType"=@IntentType
              AND "Generation"=@Generation
              AND "GenerationIncarnationId"=@GenerationIncarnationId
              AND "AttemptId"=@AttemptId
              AND "Owner" IS NOT DISTINCT FROM @Owner
              AND "LockedUntil"=@LockedUntil;
            """;
        object[] parameters =
        [
            new NpgsqlParameter("@IsInboxOrphaned", orphaned),
            new NpgsqlParameter("@Id", fence.StorageId),
            new NpgsqlParameter("@IntentType", NpgsqlDbType.Smallint)
            {
                Value = MessageLaneCompatibility.ToPersistedValue(fence.Lane),
            },
            new NpgsqlParameter("@Generation", fence.Generation),
            new NpgsqlParameter("@GenerationIncarnationId", fence.GenerationIncarnationId),
            new NpgsqlParameter("@AttemptId", fence.AttemptId),
            new NpgsqlParameter("@Owner", NpgsqlDbType.Varchar) { Value = fence.Owner ?? (object)DBNull.Value },
            new NpgsqlParameter("@LockedUntil", NpgsqlDbType.TimestampTz) { Value = fence.LockedUntil },
        ];

        await using var connection = postgreSqlOptions.Value.CreateConnection();
        var changed = await connection
            .ExecuteNonQueryAsync(
                sql,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: parameters,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (changed == 1)
        {
            message.IsInboxOrphaned = orphaned;
        }

        return changed == 1;
    }

    private string? _ValidateInboxAdmission(
        string name,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation
    )
    {
        _ValidateInboxIdentity(name, _InboxIdentityMaxLength, nameof(name));
        _ValidateInboxIdentity(consumerIdentity, _InboxIdentityMaxLength, nameof(consumerIdentity));
        _ValidateInboxIdentity(contractVersion, _InboxContractVersionMaxLength, nameof(contractVersion));
        _ValidateInboxIdentity(message.Origin.Id, MessageOptions.MessageIdMaxLength, "message.Origin.Id");
        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                "Inbox generation cannot be negative."
            );
        }

        _ = MessageLaneCompatibility.ToPersistedValue(message.Lane);
        if (
            !message.Origin.Headers.TryGetValue(Headers.TenantId, out var rawTenant)
            || string.IsNullOrWhiteSpace(rawTenant)
        )
        {
            return null;
        }

        if (rawTenant.Length > MessageOptions.TenantIdMaxLength)
        {
            throw new ArgumentException(
                $"Inbox tenant identity must be {MessageOptions.TenantIdMaxLength} characters or fewer.",
                nameof(message)
            );
        }

        return TenantContextScope.ResolveTenantId(message.Origin.Headers, logger);
    }

    private static void _ValidateInboxIdentity(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Inbox identity values cannot be null or whitespace.", parameterName);
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Inbox identity value must be {maximumLength} characters or fewer.",
                parameterName
            );
        }
    }

    private async ValueTask<(MediumMessage Message, StatusName Status)> _ReadInboxGenerationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool tenantPresent,
        string tenantId,
        string messageId,
        short intentType,
        string contractIdentity,
        string contractVersion,
        string consumerIdentity,
        long generation,
        CancellationToken cancellationToken
    )
    {
        var sql = $"""
            SELECT "Id","Content","IntentType","Retries","InlineAttempts","Added","ExpiresAt","NextRetryAt",
                   "LockedUntil","Owner","StatusName","ExceptionInfo","TenantPresent","TenantId","MessageId",
                   "ContractIdentity","ContractVersion","ConsumerIdentity","Generation","GenerationIncarnationId",
                   "AttemptId","IsInboxOrphaned"
            FROM {_receivedTable}
            WHERE "TenantPresent"=@TenantPresent AND "TenantId"=@TenantId AND "MessageId"=@MessageId
              AND "IntentType"=@IntentType AND "ContractIdentity"=@ContractIdentity
              AND "ContractVersion"=@ContractVersion AND "ConsumerIdentity"=@ConsumerIdentity
              AND "Generation"=@Generation;
            """;
        object[] parameters =
        [
            new NpgsqlParameter("@TenantPresent", tenantPresent),
            new NpgsqlParameter("@TenantId", tenantId),
            new NpgsqlParameter("@MessageId", messageId),
            new NpgsqlParameter("@IntentType", NpgsqlDbType.Smallint) { Value = intentType },
            new NpgsqlParameter("@ContractIdentity", contractIdentity),
            new NpgsqlParameter("@ContractVersion", contractVersion),
            new NpgsqlParameter("@ConsumerIdentity", consumerIdentity),
            new NpgsqlParameter("@Generation", generation),
        ];

        return await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    if (!await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(
                            "Inbox admission converged without an authoritative generation row."
                        );
                    }

                    var lane = MessageLaneCompatibility.FromPersistedValue(reader.GetInt16(2));
                    var storedTenantPresent = reader.GetBoolean(12);
                    var storedTenant = reader.GetString(13);
                    var storedGeneration = reader.GetInt64(18);
                    var incarnationId = reader.GetGuid(19);
                    var lockedUntil = reader.IsDBNull(8)
                        ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(8);
                    var owner = reader.IsDBNull(9) ? null : reader.GetString(9);
                    var attemptId = reader.IsDBNull(20) ? (Guid?)null : reader.GetGuid(20);
                    var medium = new MediumMessage
                    {
                        StorageId = reader.GetGuid(0),
                        Origin = serializer.Deserialize(reader.GetString(1))!,
                        Content = reader.GetString(1),
                        Lane = lane,
                        Retries = reader.GetInt32(3),
                        InlineAttempts = reader.GetInt32(4),
                        Added = reader.GetFieldValue<DateTimeOffset>(5),
                        ExpiresAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                        NextRetryAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                        LockedUntil = lockedUntil,
                        Owner = owner,
                        ExceptionInfo = reader.IsDBNull(11) ? null : reader.GetString(11),
                        InboxKey = new InboxKey(
                            storedTenantPresent ? storedTenant : null,
                            reader.GetString(14),
                            lane,
                            reader.GetString(15),
                            reader.GetString(16),
                            reader.GetString(17),
                            storedGeneration
                        ),
                        InboxGeneration = new InboxGeneration(storedGeneration, incarnationId),
                        IsInboxOrphaned = reader.GetBoolean(21),
                    };
                    if (attemptId is { } persistedAttempt && lockedUntil is { } persistedLockedUntil)
                    {
                        medium.InboxAttemptFence = new InboxAttemptFence(
                            medium.StorageId,
                            lane,
                            storedGeneration,
                            incarnationId,
                            persistedAttempt,
                            owner,
                            persistedLockedUntil
                        );
                    }

                    return (medium, Enum.Parse<StatusName>(reader.GetString(10), ignoreCase: false));
                },
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: parameters,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
#pragma warning restore CA1849, VSTHRD103, AsyncFixer02, MA0042
