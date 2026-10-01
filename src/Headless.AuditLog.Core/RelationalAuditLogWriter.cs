// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Headless.Serializer;
using Headless.Sql;

namespace Headless.AuditLog;

/// <summary>Writes audit rows with multi-row <c>INSERT</c> statements, on the caller's transaction or its own.</summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalAuditLogWriter(RelationalAuditLogTable table, IJsonSerializer serializer)
{
    // SQL Server allows 2,100 parameters per statement: 100 rows use 1,600.
    private const int _MaxRowsPerCommand = 100;

    private static readonly string[] _Columns =
    [
        "CreatedAt",
        "UserId",
        "AccountId",
        "TenantId",
        "IpAddress",
        "UserAgent",
        "CorrelationId",
        "Action",
        "ChangeType",
        "EntityType",
        "EntityId",
        "OldValues",
        "NewValues",
        "ChangedFields",
        "Success",
        "ErrorCode",
    ];

    // Written text is unbounded: the values are already truncated to their limits, and a parameter sized to the column
    // would silently truncate anything that was not.
    private static readonly SqlColumnType _Written = SqlColumnType.Text(-1);

    private readonly ISqlDialect _dialect = table.Dialect;
    private readonly ConcurrentDictionary<int, string> _sqlByRowCount = new();

    /// <summary>
    /// Writes audit entries. When <paramref name="sharedConnection"/> and <paramref name="sharedTransaction"/> are
    /// non-null, reuses them, so the rows commit or roll back with the caller's transaction. Otherwise opens its own
    /// connection and transaction.
    /// </summary>
    public async Task WriteAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        DbConnection? sharedConnection = null,
        DbTransaction? sharedTransaction = null,
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

        await using var connection = _dialect.CreateConnection(table.Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _WriteBatchedAsync(entries, connection, transaction, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

#pragma warning disable MA0045 // This sync API intentionally avoids blocking on the async writer path.
    /// <summary>
    /// The synchronous counterpart of <see cref="WriteAsync"/>, for the sync <c>IAuditLogStore.Save</c> path, so it
    /// never blocks on the async path.
    /// </summary>
    public void WriteSync(
        IReadOnlyList<AuditLogEntryData> entries,
        DbConnection? sharedConnection = null,
        DbTransaction? sharedTransaction = null
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

        using var connection = _dialect.CreateConnection(table.Options.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        _WriteBatchedSync(entries, connection, transaction);

        transaction.Commit();
    }

    private void _WriteBatchedSync(
        IReadOnlyList<AuditLogEntryData> entries,
        DbConnection connection,
        DbTransaction transaction
    )
    {
        for (var offset = 0; offset < entries.Count; offset += _MaxRowsPerCommand)
        {
            using var command = _CreateCommand(connection, transaction, entries, offset);
            command.ExecuteNonQuery();
        }
    }
#pragma warning restore MA0045

    private async Task _WriteBatchedAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        for (var offset = 0; offset < entries.Count; offset += _MaxRowsPerCommand)
        {
            await using var command = _CreateCommand(connection, transaction, entries, offset);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private DbCommand _CreateCommand(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyList<AuditLogEntryData> entries,
        int offset
    )
    {
        var rowCount = Math.Min(_MaxRowsPerCommand, entries.Count - offset);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = _sqlByRowCount.GetOrAdd(rowCount, _BuildInsertSql);
        command.CommandTimeout = table.Options.CommandTimeoutSeconds;

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            _AddParameters(command, entries[offset + rowIndex], rowIndex);
        }

        return command;
    }

    private void _AddParameters(DbCommand command, AuditLogEntryData entry, int rowIndex)
    {
        table.BindCreatedAt(command, _Name("CreatedAt", rowIndex), entry.CreatedAt);
        _AddText(command, "UserId", rowIndex, AuditLogFieldLimits.Truncate(entry.UserId, AuditLogFieldLimits.UserId));
        _AddText(
            command,
            "AccountId",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.AccountId, AuditLogFieldLimits.AccountId)
        );
        _AddText(
            command,
            "TenantId",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.TenantId, AuditLogFieldLimits.TenantId)
        );
        _AddText(
            command,
            "IpAddress",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.IpAddress, AuditLogFieldLimits.IpAddress)
        );
        _AddText(
            command,
            "UserAgent",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.UserAgent, AuditLogFieldLimits.UserAgent)
        );
        _AddText(
            command,
            "CorrelationId",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.CorrelationId, AuditLogFieldLimits.CorrelationId)
        );
        _AddText(command, "Action", rowIndex, AuditLogFieldLimits.Truncate(entry.Action, AuditLogFieldLimits.Action));
        _dialect.AddParameter(
            command,
            _Name("ChangeType", rowIndex),
            SqlColumnType.Int32,
            entry.ChangeType is null ? null : (int)entry.ChangeType.Value
        );
        _AddText(
            command,
            "EntityType",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.EntityType, AuditLogFieldLimits.EntityType)
        );
        _AddText(
            command,
            "EntityId",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.EntityId, AuditLogFieldLimits.EntityId)
        );
        _AddText(command, "OldValues", rowIndex, serializer.SerializeToString(entry.OldValues));
        _AddText(command, "NewValues", rowIndex, serializer.SerializeToString(entry.NewValues));
        _AddText(command, "ChangedFields", rowIndex, serializer.SerializeToString(entry.ChangedFields));
        _dialect.AddParameter(command, _Name("Success", rowIndex), SqlColumnType.Boolean, entry.Success);
        _AddText(
            command,
            "ErrorCode",
            rowIndex,
            AuditLogFieldLimits.Truncate(entry.ErrorCode, AuditLogFieldLimits.ErrorCode)
        );
    }

    private void _AddText(DbCommand command, string column, int rowIndex, string? value)
    {
        _dialect.AddParameter(command, _Name(column, rowIndex), _Written, value);
    }

    private string _BuildInsertSql(int rowCount)
    {
        var builder = new StringBuilder(256 + (rowCount * 256))
            .Append("INSERT INTO ")
            .Append(table.Qualified)
            .Append(" (")
            .AppendJoin(", ", _Columns.Select(table.Column))
            .Append(") VALUES ");

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            builder.Append(rowIndex > 0 ? ", (" : "(");

            for (var i = 0; i < _Columns.Length; i++)
            {
                var column = _Columns[i];
                var parameter = "@" + _Name(column, rowIndex);

                if (i > 0)
                {
                    builder.Append(", ");
                }

                // The JSON values are bound as text and cast to the configured column type, which PostgreSQL's
                // jsonb and json columns require and SQL Server's nvarchar(max) accepts.
                builder.Append(
                    column is "OldValues" or "NewValues" or "ChangedFields"
                        ? $"CAST({parameter} AS {table.JsonColumnType})"
                        : parameter
                );
            }

            builder.Append(')');
        }

        return builder.Append(';').ToString();
    }

    private static string _Name(string column, int rowIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{column}_{rowIndex}");
    }
}
#pragma warning restore CA2100
