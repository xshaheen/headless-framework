// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Serializer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerAuditLogWriter(
    IOptions<SqlServerAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions,
    IJsonSerializer serializer
)
{
    private const int _MaxRowsPerCommand = 100;

    private readonly ConcurrentDictionary<int, string> _sqlByRowCount = new();

    /// <summary>
    /// Writes audit entries to the audit table. When <paramref name="sharedConnection"/> and
    /// <paramref name="sharedTransaction"/> are non-null, reuses them (atomic with the caller's
    /// transaction). Otherwise opens its own connection + transaction (standalone path).
    /// </summary>
    public async Task WriteAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        SqlConnection? sharedConnection = null,
        SqlTransaction? sharedTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (sharedConnection is not null && sharedTransaction is not null)
        {
            await _WriteBatchedAsync(entries, sharedConnection, sharedTransaction, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _WriteBatchedAsync(entries, connection, transaction, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

#pragma warning disable MA0045 // This sync API intentionally avoids blocking on the async writer path.
    /// <summary>
    /// True-sync writer using <see cref="SqlCommand.ExecuteNonQuery"/>. Used by the sync
    /// <c>IAuditLogStore.Save</c> path to avoid <c>.GetAwaiter().GetResult()</c> on the async path.
    /// When <paramref name="sharedConnection"/> and <paramref name="sharedTransaction"/> are
    /// supplied, writes through the caller's connection (atomic enrollment).
    /// </summary>
    public void WriteSync(
        IReadOnlyList<AuditLogEntryData> entries,
        SqlConnection? sharedConnection = null,
        SqlTransaction? sharedTransaction = null
    )
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (sharedConnection is not null && sharedTransaction is not null)
        {
            _WriteBatchedSync(entries, sharedConnection, sharedTransaction);
            return;
        }

        using var connection = providerOptions.Value.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        _WriteBatchedSync(entries, connection, transaction);

        transaction.Commit();
    }

    private async Task _WriteBatchedAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        for (var offset = 0; offset < entries.Count; offset += _MaxRowsPerCommand)
        {
            var rowCount = Math.Min(_MaxRowsPerCommand, entries.Count - offset);
            var sql = _sqlByRowCount.GetOrAdd(rowCount, _BuildInsertSql);

            await using var command = new SqlCommand(sql, connection, transaction);
            _AddParameters(command, entries, offset, rowCount);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void _WriteBatchedSync(
        IReadOnlyList<AuditLogEntryData> entries,
        SqlConnection connection,
        SqlTransaction transaction
    )
    {
        for (var offset = 0; offset < entries.Count; offset += _MaxRowsPerCommand)
        {
            var rowCount = Math.Min(_MaxRowsPerCommand, entries.Count - offset);
            var sql = _sqlByRowCount.GetOrAdd(rowCount, _BuildInsertSql);

            using var command = new SqlCommand(sql, connection, transaction);
            _AddParameters(command, entries, offset, rowCount);
            command.ExecuteNonQuery();
        }
    }
#pragma warning restore MA0045

    private string _BuildInsertSql(int rowCount)
    {
        var table = SqlServerAuditLogStorageInitializer.Qualified(storageOptions.Value);

        var builder = new StringBuilder(256 + rowCount * 220);
        builder.Append("INSERT INTO ").Append(table);
        builder.Append(
            " ([CreatedAt],[UserId],[AccountId],[TenantId],[IpAddress],[UserAgent],[CorrelationId],[Action],[ChangeType],[EntityType],[EntityId],[OldValues],[NewValues],[ChangedFields],[Success],[ErrorCode]) VALUES "
        );

        for (var i = 0; i < rowCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append("(@CreatedAt_")
                .Append(i)
                .Append(",@UserId_")
                .Append(i)
                .Append(",@AccountId_")
                .Append(i)
                .Append(",@TenantId_")
                .Append(i)
                .Append(",@IpAddress_")
                .Append(i)
                .Append(",@UserAgent_")
                .Append(i)
                .Append(",@CorrelationId_")
                .Append(i)
                .Append(",@Action_")
                .Append(i)
                .Append(",@ChangeType_")
                .Append(i)
                .Append(",@EntityType_")
                .Append(i)
                .Append(",@EntityId_")
                .Append(i)
                .Append(",@OldValues_")
                .Append(i)
                .Append(",@NewValues_")
                .Append(i)
                .Append(",@ChangedFields_")
                .Append(i)
                .Append(",@Success_")
                .Append(i)
                .Append(",@ErrorCode_")
                .Append(i)
                .Append(')');
        }

        builder.Append(';');
        return builder.ToString();
    }

    private void _AddParameters(SqlCommand command, IReadOnlyList<AuditLogEntryData> entries, int offset, int rowCount)
    {
        var parameters = command.Parameters;

        for (var i = 0; i < rowCount; i++)
        {
            var entry = entries[offset + i];
            parameters.Add(
                _Param(string.Create(CultureInfo.InvariantCulture, $"CreatedAt_{i}"), entry.CreatedAt.UtcDateTime)
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"UserId_{i}"),
                    AuditLogFieldLimits.Truncate(entry.UserId, AuditLogFieldLimits.UserId)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"AccountId_{i}"),
                    AuditLogFieldLimits.Truncate(entry.AccountId, AuditLogFieldLimits.AccountId)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"TenantId_{i}"),
                    AuditLogFieldLimits.Truncate(entry.TenantId, AuditLogFieldLimits.TenantId)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"IpAddress_{i}"),
                    AuditLogFieldLimits.Truncate(entry.IpAddress, AuditLogFieldLimits.IpAddress)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"UserAgent_{i}"),
                    AuditLogFieldLimits.Truncate(entry.UserAgent, AuditLogFieldLimits.UserAgent)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"CorrelationId_{i}"),
                    AuditLogFieldLimits.Truncate(entry.CorrelationId, AuditLogFieldLimits.CorrelationId)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"Action_{i}"),
                    AuditLogFieldLimits.Truncate(entry.Action, AuditLogFieldLimits.Action)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"ChangeType_{i}"),
                    entry.ChangeType is null ? null : (int)entry.ChangeType.Value
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"EntityType_{i}"),
                    AuditLogFieldLimits.Truncate(entry.EntityType, AuditLogFieldLimits.EntityType)
                )
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"EntityId_{i}"),
                    AuditLogFieldLimits.Truncate(entry.EntityId, AuditLogFieldLimits.EntityId)
                )
            );
            parameters.Add(
                _Param(string.Create(CultureInfo.InvariantCulture, $"OldValues_{i}"), _Serialize(entry.OldValues))
            );
            parameters.Add(
                _Param(string.Create(CultureInfo.InvariantCulture, $"NewValues_{i}"), _Serialize(entry.NewValues))
            );
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"ChangedFields_{i}"),
                    _Serialize(entry.ChangedFields)
                )
            );
            parameters.Add(_Param(string.Create(CultureInfo.InvariantCulture, $"Success_{i}"), entry.Success));
            parameters.Add(
                _Param(
                    string.Create(CultureInfo.InvariantCulture, $"ErrorCode_{i}"),
                    AuditLogFieldLimits.Truncate(entry.ErrorCode, AuditLogFieldLimits.ErrorCode)
                )
            );
        }
    }

    private string? _Serialize<T>(T? value) => serializer.SerializeToString(value);

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
