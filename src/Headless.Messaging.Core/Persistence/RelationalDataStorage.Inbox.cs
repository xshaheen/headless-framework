// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA2100 // SQL text is rendered from dialect output, table names, and fixed fragments; every value is a parameter.
#pragma warning disable CA1849, VSTHRD103, AsyncFixer02, MA0042 // Once a row is buffered, these small typed reads cannot add blocking I/O.

internal sealed partial class RelationalDataStorage
{
    private const int _ContractVersionMaxLength = 100;
    private const int _InboxKeyHashLength = 32;

    public async ValueTask<InboxAdmissionResult> AdmitReceivedMessageAsync(
        string name,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation = 0,
        TimeSpan? inboxRetention = null,
        CancellationToken cancellationToken = default
    )
    {
        var tenantId = InboxAdmissionValidation.Validate(name, consumerIdentity, contractVersion, message, generation);
        var key = new InboxRootKey(
            tenantId is not null,
            tenantId ?? string.Empty,
            message.Origin.Id,
            MessageLaneCompatibility.ToPersistedValue(message.Lane),
            name,
            contractVersion,
            consumerIdentity,
            generation
        );
        var storageId = _guidGenerator.Create();
        var incarnationId = _guidGenerator.Create();
        var content = _serializer.Serialize(message.Origin);
        var retentionSeconds = _ValidateInboxRetention(inboxRetention);
        var keyHash = _CreateInboxKeyHash(key, lifecycleId: null);

        // The root key is unique over inbox root generations only (a replay child carries its own lineage), so the
        // insert's conflict target is that partial index, and the insert waits out a concurrent admission of the same
        // key instead of failing on it. The database clock stamps the row and the grace before its first dispatch.
        var insertSql = _dialect.Render(
            new SqlInsertIfAbsent(
                _receivedTable,
                [new SqlKeyColumn(_t.InboxKeyHash, "InboxKeyHash")],
                [
                    _t.Id,
                    _t.Version,
                    _t.Name,
                    _t.Content,
                    _t.IntentType,
                    _t.Retries,
                    _t.InlineAttempts,
                    _t.Added,
                    _t.ExpiresAt,
                    _t.NextRetryAt,
                    _t.LockedUntil,
                    _t.Owner,
                    _t.StatusName,
                    _t.MessageId,
                    _t.ExceptionInfo,
                    _t.TenantPresent,
                    _t.TenantId,
                    _t.ContractIdentity,
                    _t.ContractVersion,
                    _t.ConsumerIdentity,
                    _t.Generation,
                    _t.GenerationIncarnationId,
                    _t.LifecycleId,
                    _t.AttemptId,
                    _t.IsInboxOrphaned,
                    _t.IsCurrentGeneration,
                    _t.IsInboxRecord,
                    _t.InboxRetentionSeconds,
                ],
                [
                    "@Id",
                    "@Version",
                    "@Name",
                    "@Content",
                    "@IntentType",
                    "0",
                    "0",
                    SqlDialectTokens.Now,
                    "NULL",
                    _dialect.ShiftByDuration(SqlDialectTokens.Now, "Grace"),
                    "NULL",
                    "NULL",
                    "@StatusName",
                    "@MessageId",
                    "NULL",
                    "@TenantPresent",
                    "@TenantId",
                    "@ContractIdentity",
                    "@ContractVersion",
                    "@ConsumerIdentity",
                    "@Generation",
                    "@GenerationIncarnationId",
                    "@GenerationIncarnationId",
                    "NULL",
                    _t.False,
                    _t.True,
                    _t.True,
                    "@InboxRetentionSeconds",
                ],
                [_t.Id],
                InboxRootPredicate
            )
        );

        var (inserted, stored) = await SqlAutonomousTransaction
            .RunAsync(
                "messaging.admit_received_message",
                _CreateConnection,
                async (connection, transaction, ct) =>
                {
                    var admitted = await RelationalCommand
                        .ExecuteReaderAsync(
                            connection,
                            transaction,
                            insertSql,
                            CommandTimeoutSeconds,
                            command =>
                            {
                                _BindInboxRootKey(command, key, keyHash);
                                _dialect.AddParameter(command, "Id", SqlColumnType.Guid, storageId);
                                _BindVersion(command);
                                _dialect.AddParameter(command, "Name", _NameType, name);
                                _dialect.AddParameter(command, "Content", _ContentType, content);
                                _dialect.AddDuration(command, "Grace", Options.RetryPolicy.InitialDispatchGrace);
                                _dialect.AddParameter(command, "StatusName", _StatusType, nameof(StatusName.Scheduled));
                                _dialect.AddParameter(
                                    command,
                                    "GenerationIncarnationId",
                                    SqlColumnType.Guid,
                                    incarnationId
                                );
                                _dialect.AddParameter(
                                    command,
                                    "InboxRetentionSeconds",
                                    SqlColumnType.Int64,
                                    retentionSeconds
                                );
                            },
                            _ReadAppliedAsync,
                            ct
                        )
                        .ConfigureAwait(false);

                    var row = await _ReadInboxGenerationAsync(connection, transaction, key, keyHash, message.Origin, ct)
                        .ConfigureAwait(false);

                    return (admitted, row);
                },
                _timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);

        var disposition = inserted
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

    /// <summary>The filter of the root-key unique index: inbox root generations, never replay children.</summary>
    private string InboxRootPredicate => $"{_t.IsInboxRecord} = {_t.True} AND {_t.ReplayParentIncarnationId} IS NULL";

    /// <summary>An inbox root generation's logical identity, as the admission and its read-back match it.</summary>
    private sealed record InboxRootKey(
        bool TenantPresent,
        string TenantId,
        string MessageId,
        short IntentType,
        string ContractIdentity,
        string ContractVersion,
        string ConsumerIdentity,
        long Generation
    );

    private void _BindInboxRootKey(DbCommand command, InboxRootKey key, byte[] keyHash)
    {
        _dialect.AddParameter(command, "InboxKeyHash", SqlColumnType.FixedBinary(_InboxKeyHashLength), keyHash);
        _dialect.AddParameter(command, "TenantPresent", SqlColumnType.Boolean, key.TenantPresent);
        _dialect.AddParameter(command, "TenantId", SqlColumnType.KeyText(_NameMaxLength), key.TenantId);
        _dialect.AddParameter(command, "MessageId", _MessageIdType, key.MessageId);
        _dialect.AddParameter(command, "IntentType", SqlColumnType.Int16, key.IntentType);
        _dialect.AddParameter(command, "ContractIdentity", SqlColumnType.KeyText(_NameMaxLength), key.ContractIdentity);
        _dialect.AddParameter(
            command,
            "ContractVersion",
            SqlColumnType.KeyText(_ContractVersionMaxLength),
            key.ContractVersion
        );
        _dialect.AddParameter(command, "ConsumerIdentity", _IdentityType, key.ConsumerIdentity);
        _dialect.AddParameter(command, "Generation", SqlColumnType.Int64, key.Generation);
    }

    /// <summary>
    /// The SHA-256 of an inbox generation's identity, which the root-key unique index is built on: the identity's text
    /// columns together are too wide for an index key on every engine. Each value is length-prefixed so no two
    /// identities share a canonical form. A replay child hashes under its lineage, so it never collides with a root.
    /// </summary>
    private static byte[] _CreateInboxKeyHash(InboxRootKey key, Guid? lifecycleId)
    {
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"{(key.TenantPresent ? 1 : 0)}:{key.TenantId.Length}:{key.TenantId}{key.MessageId.Length}:{key.MessageId}{key.IntentType}:{key.ContractIdentity.Length}:{key.ContractIdentity}{key.ContractVersion.Length}:{key.ContractVersion}{key.ConsumerIdentity.Length}:{key.ConsumerIdentity}{key.Generation}"
        );
        if (lifecycleId is { } lifecycle)
        {
            canonical = $"replay:{lifecycle:N}:{canonical}";
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
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

    private async Task<(MediumMessage Message, StatusName Status)> _ReadInboxGenerationAsync(
        DbConnection connection,
        DbTransaction transaction,
        InboxRootKey key,
        byte[] keyHash,
        Message redelivery,
        CancellationToken cancellationToken
    )
    {
        // The hash seeks the root-key index; the column comparisons make the match exact whatever the hash.
        var sql = $"""
            SELECT {_t.Id},{_t.Content},{_t.IntentType},{_t.Retries},{_t.InlineAttempts},{_t.Added},{_t.ExpiresAt},{_t.NextRetryAt},{_t.LockedUntil},{_t.Owner},{_t.StatusName},{_t.ExceptionInfo},{_t.TenantPresent},{_t.TenantId},{_t.MessageId},{_t.ContractIdentity},{_t.ContractVersion},{_t.ConsumerIdentity},{_t.Generation},{_t.GenerationIncarnationId},{_t.AttemptId},{_t.IsInboxOrphaned}
            FROM {_receivedTable}
            WHERE {_t.InboxKeyHash}=@InboxKeyHash
              AND {_t.TenantPresent}=@TenantPresent AND {_t.TenantId}=@TenantId AND {_t.MessageId}=@MessageId
              AND {_t.IntentType}=@IntentType AND {_t.ContractIdentity}=@ContractIdentity
              AND {_t.ContractVersion}=@ContractVersion AND {_t.ConsumerIdentity}=@ConsumerIdentity
              AND {_t.Generation}=@Generation
              AND {InboxRootPredicate};
            """;

        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command => _BindInboxRootKey(command, key, keyHash),
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
                    var status = Enum.Parse<StatusName>(reader.GetString(10), ignoreCase: false);
                    var isTerminal = status is StatusName.Succeeded or StatusName.Failed && reader.IsDBNull(7);
                    // Terminal duplicates never execute. Their retained payload may be the poison that
                    // ended the generation; use this delivery's envelope without rewriting stored evidence.
                    var medium = new MediumMessage
                    {
                        StorageId = reader.GetGuid(0),
                        Origin = isTerminal ? redelivery : _serializer.Deserialize(reader.GetString(1))!,
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

                    return (medium, status);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}

#pragma warning restore CA1849, VSTHRD103, AsyncFixer02, MA0042, CA2100
