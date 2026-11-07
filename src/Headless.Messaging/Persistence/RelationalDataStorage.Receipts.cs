// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

#pragma warning disable CA1849 // Buffered row reads cannot add blocking I/O.

// The receipt and audit rows of the operations that target one stored row: a published re-send or delete and a
// scheduled-delivery revoke or early dispatch. Each kind maps the shared receipt to its own result type.
internal sealed partial class RelationalDataStorage
{
    /// <summary>
    /// A row-targeted operation's receipt, or the request it is compared with. <see cref="ExpectedDueAt"/> is set only by
    /// a kind fenced on a due instant, and only such a kind reads and writes that column.
    /// </summary>
    private sealed record RowOperationReceipt(
        Guid OperationId,
        string TargetKind,
        MessagingOperationType OperationType,
        InboxOperationOutcome Outcome,
        Guid StorageId,
        DateTimeOffset? ExpectedDueAt,
        string? MessageName,
        string? MessageId,
        MessageLane? Lane,
        string Actor,
        string? Reason,
        DateTimeOffset CreatedAt
    );

    /// <summary>
    /// Returns what a request under an already used operation id gets: the stored receipt when the request repeats it,
    /// otherwise a conflict, audited, that changes nothing. Returns <see langword="null"/> for an unused id. The caller
    /// holds the operation id's lock.
    /// </summary>
    private async Task<RowOperationReceipt?> _ReplayRowOperationAsync(
        DbConnection connection,
        DbTransaction transaction,
        RowOperationReceipt attempt,
        CancellationToken cancellationToken
    )
    {
        var prior = await _ReadRowOperationReceiptAsync(
                connection,
                transaction,
                attempt.OperationId,
                withExpectedDueAt: attempt.ExpectedDueAt is not null,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (prior is null)
        {
            return null;
        }

        // A receipt of another target kind under this id never replays, even for an operation type both kinds share.
        var matches =
            string.Equals(prior.TargetKind, attempt.TargetKind, StringComparison.Ordinal)
            && prior.OperationType == attempt.OperationType
            && prior.StorageId == attempt.StorageId
            && prior.ExpectedDueAt == attempt.ExpectedDueAt
            && string.Equals(prior.Actor, attempt.Actor, StringComparison.Ordinal)
            && string.Equals(prior.Reason, attempt.Reason, StringComparison.Ordinal);
        if (matches)
        {
            return prior;
        }

        var conflict = attempt with
        {
            Outcome = InboxOperationOutcome.OperationConflict,
            MessageName = prior.MessageName,
            MessageId = prior.MessageId,
            Lane = prior.Lane,
            CreatedAt = prior.CreatedAt,
        };
        await _WriteConflictAuditAsync(
                connection,
                transaction,
                conflict.TargetKind,
                conflict.OperationId,
                incarnationId: null,
                conflict.OperationType,
                conflict.Actor,
                conflict.Reason,
                conflict.Outcome,
                cancellationToken
            )
            .ConfigureAwait(false);

        return conflict;
    }

    private async Task<RowOperationReceipt?> _ReadRowOperationReceiptAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid operationId,
        bool withExpectedDueAt,
        CancellationToken cancellationToken
    )
    {
        string[] columns = withExpectedDueAt
            ?
            [
                _t.TargetKind,
                _t.OperationType,
                _t.Outcome,
                _t.StorageId,
                _t.ExpectedDueAt,
                _t.MessageName,
                _t.MessageId,
                _t.Lane,
                _t.Actor,
                _t.Reason,
                _t.CreatedAt,
            ]
            :
            [
                _t.TargetKind,
                _t.OperationType,
                _t.Outcome,
                _t.StorageId,
                _t.MessageName,
                _t.MessageId,
                _t.Lane,
                _t.Actor,
                _t.Reason,
                _t.CreatedAt,
            ];
        var sql = _dialect.Render(
            new SqlLockedRead(_t.Receipts, [new SqlKeyColumn(_t.OperationId, "OperationId")], columns)
        );

        return await RelationalCommand
            .ExecuteReaderAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command => _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, operationId),
                async (reader, ct) =>
                {
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    // The columns after the due instant shift by one when it is read.
                    var after = withExpectedDueAt ? 5 : 4;

                    return new RowOperationReceipt(
                        operationId,
                        reader.GetString(0),
                        Enum.Parse<MessagingOperationType>(reader.GetString(1)),
                        Enum.Parse<InboxOperationOutcome>(reader.GetString(2)),
                        reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3),
                        withExpectedDueAt && !reader.IsDBNull(4) ? reader.GetFieldValue<DateTimeOffset>(4) : null,
                        reader.IsDBNull(after) ? null : reader.GetString(after),
                        reader.IsDBNull(after + 1) ? null : reader.GetString(after + 1),
                        reader.IsDBNull(after + 2) ? null : Enum.Parse<MessageLane>(reader.GetString(after + 2)),
                        reader.GetString(after + 3),
                        reader.IsDBNull(after + 4) ? null : reader.GetString(after + 4),
                        reader.GetFieldValue<DateTimeOffset>(after + 5)
                    );
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Writes the receipt and its audit row in the transaction that changed, or refused to change, the row.</summary>
    private async Task _WriteRowOperationReceiptAndAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        RowOperationReceipt receipt,
        CancellationToken cancellationToken
    )
    {
        var dueColumn = receipt.ExpectedDueAt is null ? "" : $"{_t.ExpectedDueAt},";
        var dueValue = receipt.ExpectedDueAt is null ? "" : "@ExpectedDueAt,";
        var sql = $"""
            INSERT INTO {_t.Receipts}({_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Outcome},{_t.Actor},{_t.Reason},{dueColumn}{_t.StorageId},{_t.MessageName},{_t.MessageId},{_t.Lane},{_t.CreatedAt})
            VALUES (@OperationId,'{receipt.TargetKind}',@OperationType,@Outcome,@Actor,@Reason,{dueValue}@StorageId,@MessageName,@MessageId,@Lane,@CreatedAt);
            INSERT INTO {_t.Audit}({_t.AuditId},{_t.OperationId},{_t.TargetKind},{_t.OperationType},{_t.Actor},{_t.Reason},{_t.Outcome},{_t.CreatedAt})
            VALUES (@AuditId,@OperationId,'{receipt.TargetKind}',@OperationType,@Actor,@Reason,@Outcome,@CreatedAt);
            """;

        await RelationalCommand
            .ExecuteNonQueryAsync(
                connection,
                transaction,
                sql,
                CommandTimeoutSeconds,
                command =>
                {
                    _dialect.AddParameter(command, "AuditId", SqlColumnType.Guid, _guidGenerator.Create());
                    _dialect.AddParameter(command, "OperationId", SqlColumnType.Guid, receipt.OperationId);
                    _dialect.AddParameter(command, "OperationType", _StatusType, receipt.OperationType.ToString());
                    _dialect.AddParameter(command, "Outcome", _StatusType, receipt.Outcome.ToString());
                    _dialect.AddParameter(command, "Actor", SqlColumnType.KeyText(_ActorMaxLength), receipt.Actor);
                    _dialect.AddParameter(command, "Reason", SqlColumnType.Text(_ReasonMaxLength), receipt.Reason);
                    if (receipt.ExpectedDueAt is { } expectedDueAt)
                    {
                        _dialect.AddParameter(command, "ExpectedDueAt", SqlColumnType.Timestamp, expectedDueAt);
                    }

                    _dialect.AddParameter(command, "StorageId", SqlColumnType.Guid, receipt.StorageId);
                    _dialect.AddParameter(command, "MessageName", _NameType, receipt.MessageName);
                    _dialect.AddParameter(command, "MessageId", _MessageIdType, receipt.MessageId);
                    _dialect.AddParameter(command, "Lane", _StatusType, receipt.Lane?.ToString());
                    _dialect.AddParameter(command, "CreatedAt", SqlColumnType.Timestamp, receipt.CreatedAt);
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}

#pragma warning restore CA1849
