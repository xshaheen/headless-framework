// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;

#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // Once a row is buffered, these small typed reads cannot add blocking I/O.
namespace Headless.Messaging.Storage.SqlServer;

internal sealed partial class SqlServerDataStorage
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
        (DateTimeOffset LockedUntil, string? Owner, Guid? AttemptId) storedLease
    )
    {
        message.LockedUntil = storedLease.LockedUntil;
        message.Owner = storedLease.Owner;
        if (storedLease.AttemptId is { } attemptId && message.InboxGeneration is { } generation)
        {
            message.InboxAttemptFence = new InboxAttemptFence(
                message.StorageId,
                message.Lane,
                generation.Number,
                generation.IncarnationId,
                attemptId,
                storedLease.Owner,
                storedLease.LockedUntil
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
        var inboxKeyHash = _CreateInboxKeyHash(
            tenantPresent,
            normalizedTenantId,
            message.Origin.Id,
            intentType,
            name,
            contractVersion,
            consumerIdentity,
            generation
        );
        var (graceWholeSeconds, graceNanoseconds) = _SplitLeaseDuration(
            messagingOptions.Value.RetryPolicy.InitialDispatchGrace
        );

        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var insertSql = $"""
            SET NOCOUNT ON;
            DECLARE @AdmissionNow datetimeoffset(7) = SYSUTCDATETIME();

            INSERT INTO {_receivedTable}(
                [Id],[Version],[Name],[Group],[Content],[IntentType],[Retries],[InlineAttempts],
                [Added],[ExpiresAt],[NextRetryAt],[LockedUntil],[Owner],[StatusName],[MessageId],[ExceptionInfo],
                [TenantPresent],[TenantId],[ContractIdentity],[ContractVersion],[ConsumerIdentity],[Generation],
                [GenerationIncarnationId],[AttemptId],[IsInboxOrphaned],[IsCurrentGeneration],[IsInboxRecord],[InboxKeyHash]
            )
            SELECT @Id,@Version,@Name,@Group,@Content,@IntentType,0,0,
                @AdmissionNow,NULL,DATEADD(nanosecond,@GraceNanoseconds,DATEADD(second,@GraceWholeSeconds,@AdmissionNow)),
                NULL,NULL,@StatusName,@MessageId,NULL,@TenantPresent,@TenantId,@ContractIdentity,@ContractVersion,
                @ConsumerIdentity,@Generation,@GenerationIncarnationId,NULL,0,1,1,@InboxKeyHash
            WHERE NOT EXISTS (
                SELECT 1 FROM {_receivedTable} WITH (UPDLOCK,HOLDLOCK)
                WHERE [InboxKeyHash]=@InboxKeyHash
                  AND [TenantPresent]=@TenantPresent
                  AND [TenantIdOrdinal]=CONVERT(varbinary(400),@TenantId)
                  AND [MessageIdOrdinal]=CONVERT(varbinary(400),@MessageId)
                  AND [IntentType]=@IntentType
                  AND [ContractIdentityOrdinal]=CONVERT(varbinary(400),@ContractIdentity)
                  AND [ContractVersionOrdinal]=CONVERT(varbinary(200),@ContractVersion)
                  AND [ConsumerIdentityOrdinal]=CONVERT(varbinary(400),@ConsumerIdentity)
                  AND [Generation]=@Generation
            );

            SELECT CAST(@@ROWCOUNT AS int);
            """;

        object[] parameters =
        [
            new SqlParameter("@Id", storageId),
            new SqlParameter("@Version", SqlDbType.NVarChar, 20) { Value = messagingOptions.Value.Version },
            new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = name },
            new SqlParameter("@Group", SqlDbType.NVarChar, 200) { Value = group ?? (object)DBNull.Value },
            new SqlParameter("@Content", SqlDbType.NVarChar, -1) { Value = content },
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = intentType },
            new SqlParameter("@GraceWholeSeconds", SqlDbType.Int) { Value = graceWholeSeconds },
            new SqlParameter("@GraceNanoseconds", SqlDbType.Int) { Value = graceNanoseconds },
            new SqlParameter("@StatusName", SqlDbType.NVarChar, 50) { Value = nameof(StatusName.Scheduled) },
            new SqlParameter("@MessageId", SqlDbType.NVarChar, 200) { Value = message.Origin.Id },
            new SqlParameter("@TenantPresent", SqlDbType.Bit) { Value = tenantPresent },
            new SqlParameter("@TenantId", SqlDbType.NVarChar, 200) { Value = normalizedTenantId },
            new SqlParameter("@ContractIdentity", SqlDbType.NVarChar, 200) { Value = name },
            new SqlParameter("@ContractVersion", SqlDbType.NVarChar, 100) { Value = contractVersion },
            new SqlParameter("@ConsumerIdentity", SqlDbType.NVarChar, 200) { Value = consumerIdentity },
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation },
            new SqlParameter("@GenerationIncarnationId", incarnationId),
            new SqlParameter("@InboxKeyHash", SqlDbType.Binary, 32) { Value = inboxKeyHash },
        ];

        var inserted = await connection
            .ExecuteReaderAsync(
                insertSql,
                static async (reader, token) =>
                    await reader.ReadAsync(token).ConfigureAwait(false) ? reader.GetInt32(0) : 0,
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: parameters,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        var stored = await _ReadInboxGenerationAsync(
                connection,
                (SqlTransaction)transaction,
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

        var disposition =
            inserted == 1
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
            SET [IsInboxOrphaned]=@IsInboxOrphaned
            WHERE [Id]=@Id
              AND [IntentType]=@IntentType
              AND [Generation]=@Generation
              AND [GenerationIncarnationId]=@GenerationIncarnationId
              AND [AttemptId]=@AttemptId
              AND ([Owner]=@Owner OR ([Owner] IS NULL AND @Owner IS NULL))
              AND [LockedUntil]=@LockedUntil;
            """;
        object[] parameters =
        [
            new SqlParameter("@IsInboxOrphaned", SqlDbType.Bit) { Value = orphaned },
            new SqlParameter("@Id", fence.StorageId),
            new SqlParameter("@IntentType", SqlDbType.SmallInt)
            {
                Value = MessageLaneCompatibility.ToPersistedValue(fence.Lane),
            },
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = fence.Generation },
            new SqlParameter("@GenerationIncarnationId", fence.GenerationIncarnationId),
            new SqlParameter("@AttemptId", fence.AttemptId),
            new SqlParameter("@Owner", SqlDbType.NVarChar, options.Value.OwnerColumnMaxLength)
            {
                Value = fence.Owner ?? (object)DBNull.Value,
            },
            new SqlParameter("@LockedUntil", SqlDbType.DateTimeOffset) { Value = fence.LockedUntil },
        ];

        await using var connection = new SqlConnection(options.Value.ConnectionString);
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

    private static byte[] _CreateInboxKeyHash(
        bool tenantPresent,
        string tenantId,
        string messageId,
        short intentType,
        string contractIdentity,
        string contractVersion,
        string consumerIdentity,
        long generation
    )
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{(tenantPresent ? 1 : 0)}:{tenantId.Length}:{tenantId}{messageId.Length}:{messageId}{intentType}:{contractIdentity.Length}:{contractIdentity}{contractVersion.Length}:{contractVersion}{consumerIdentity.Length}:{consumerIdentity}{generation}"
        );
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private async ValueTask<(MediumMessage Message, StatusName Status)> _ReadInboxGenerationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
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
            SELECT [Id],[Content],[IntentType],[Retries],[InlineAttempts],[Added],[ExpiresAt],[NextRetryAt],
                   [LockedUntil],[Owner],[StatusName],[ExceptionInfo],[TenantPresent],[TenantId],[MessageId],
                   [ContractIdentity],[ContractVersion],[ConsumerIdentity],[Generation],[GenerationIncarnationId],
                   [AttemptId],[IsInboxOrphaned]
            FROM {_receivedTable}
            WHERE [TenantPresent]=@TenantPresent
              AND [TenantIdOrdinal]=CONVERT(varbinary(400),@TenantId)
              AND [MessageIdOrdinal]=CONVERT(varbinary(400),@MessageId)
              AND [IntentType]=@IntentType
              AND [ContractIdentityOrdinal]=CONVERT(varbinary(400),@ContractIdentity)
              AND [ContractVersionOrdinal]=CONVERT(varbinary(200),@ContractVersion)
              AND [ConsumerIdentityOrdinal]=CONVERT(varbinary(400),@ConsumerIdentity)
              AND [Generation]=@Generation;
            """;
        object[] parameters =
        [
            new SqlParameter("@TenantPresent", SqlDbType.Bit) { Value = tenantPresent },
            new SqlParameter("@TenantId", SqlDbType.NVarChar, 200) { Value = tenantId },
            new SqlParameter("@MessageId", SqlDbType.NVarChar, 200) { Value = messageId },
            new SqlParameter("@IntentType", SqlDbType.SmallInt) { Value = intentType },
            new SqlParameter("@ContractIdentity", SqlDbType.NVarChar, 200) { Value = contractIdentity },
            new SqlParameter("@ContractVersion", SqlDbType.NVarChar, 100) { Value = contractVersion },
            new SqlParameter("@ConsumerIdentity", SqlDbType.NVarChar, 200) { Value = consumerIdentity },
            new SqlParameter("@Generation", SqlDbType.BigInt) { Value = generation },
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
