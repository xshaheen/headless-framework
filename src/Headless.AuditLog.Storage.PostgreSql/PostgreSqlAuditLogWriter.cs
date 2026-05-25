// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.AuditLog;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

internal sealed class PostgreSqlAuditLogWriter(
    IOptions<PostgreSqlAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions
)
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);
    private string? _cachedSql;

    /// <summary>
    /// Writes audit entries to the audit table. When <paramref name="sharedConnection"/> and
    /// <paramref name="sharedTransaction"/> are non-null, reuses them (atomic with the caller's
    /// transaction). Otherwise opens its own connection + transaction (standalone path).
    /// </summary>
    public async Task WriteAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        NpgsqlConnection? sharedConnection = null,
        NpgsqlTransaction? sharedTransaction = null,
        CancellationToken cancellationToken = default
    )
    {
        if (entries.Count == 0)
        {
            return;
        }

        var sql = _cachedSql ??= _BuildInsertSql();

        if (sharedConnection is not null && sharedTransaction is not null)
        {
            await _WriteOnSharedAsync(entries, sql, sharedConnection, sharedTransaction, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(_CreateParameters(entry));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True-sync writer using <see cref="NpgsqlCommand.ExecuteNonQuery"/>. Used by the sync
    /// <c>IAuditLogStore.Save</c> path to avoid <c>.GetAwaiter().GetResult()</c> on the async path.
    /// When <paramref name="sharedConnection"/> and <paramref name="sharedTransaction"/> are
    /// supplied, writes through the caller's connection (atomic enrollment).
    /// </summary>
    public void WriteSync(
        IReadOnlyList<AuditLogEntryData> entries,
        NpgsqlConnection? sharedConnection = null,
        NpgsqlTransaction? sharedTransaction = null
    )
    {
        if (entries.Count == 0)
        {
            return;
        }

        var sql = _cachedSql ??= _BuildInsertSql();

        if (sharedConnection is not null && sharedTransaction is not null)
        {
            foreach (var entry in entries)
            {
                using var command = new NpgsqlCommand(sql, sharedConnection, sharedTransaction);
                command.Parameters.AddRange(_CreateParameters(entry));
                command.ExecuteNonQuery();
            }

            return;
        }

        using var connection = providerOptions.Value.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var entry in entries)
        {
            using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(_CreateParameters(entry));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static async Task _WriteOnSharedAsync(
        IReadOnlyList<AuditLogEntryData> entries,
        string sql,
        NpgsqlConnection sharedConnection,
        NpgsqlTransaction sharedTransaction,
        CancellationToken cancellationToken
    )
    {
        foreach (var entry in entries)
        {
            await using var command = new NpgsqlCommand(sql, sharedConnection, sharedTransaction);
            command.Parameters.AddRange(_CreateParameters(entry));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private string _BuildInsertSql()
    {
        var options = storageOptions.Value;
        var table = PostgreSqlAuditLogStorageInitializer.Qualified(options);
        var jsonColumnType = (options.JsonColumnType ?? AuditLogJsonColumnType.Jsonb).ToSqlFragment();

        return $"""INSERT INTO {table} ("CreatedAt","UserId","AccountId","TenantId","IpAddress","UserAgent","CorrelationId","Action","ChangeType","EntityType","EntityId","OldValues","NewValues","ChangedFields","Success","ErrorCode") VALUES (@CreatedAt,@UserId,@AccountId,@TenantId,@IpAddress,@UserAgent,@CorrelationId,@Action,@ChangeType,@EntityType,@EntityId,CAST(@OldValues AS {jsonColumnType}),CAST(@NewValues AS {jsonColumnType}),CAST(@ChangedFields AS {jsonColumnType}),@Success,@ErrorCode);""";
    }

    private static NpgsqlParameter[] _CreateParameters(AuditLogEntryData entry) =>
    [
        _Param("CreatedAt", entry.CreatedAt),
        _Param("UserId", _Truncate(entry.UserId, 128)),
        _Param("AccountId", _Truncate(entry.AccountId, 128)),
        _Param("TenantId", _Truncate(entry.TenantId, 128)),
        _Param("IpAddress", _Truncate(entry.IpAddress, 45)),
        _Param("UserAgent", _Truncate(entry.UserAgent, 512)),
        _Param("CorrelationId", _Truncate(entry.CorrelationId, 128)),
        _Param("Action", _Truncate(entry.Action, 256)),
        _Param("ChangeType", entry.ChangeType is null ? null : (int)entry.ChangeType.Value),
        _Param("EntityType", _Truncate(entry.EntityType, 512)),
        _Param("EntityId", _Truncate(entry.EntityId, 256)),
        _Param("OldValues", _Serialize(entry.OldValues)),
        _Param("NewValues", _Serialize(entry.NewValues)),
        _Param("ChangedFields", _Serialize(entry.ChangedFields)),
        _Param("Success", entry.Success),
        _Param("ErrorCode", _Truncate(entry.ErrorCode, 256)),
    ];

    private static string? _Serialize<T>(T? value) => value is null ? null : JsonSerializer.Serialize(value, _JsonOptions);

    [return: NotNullIfNotNull(nameof(value))]
    private static string? _Truncate(string? value, int maxLength) =>
        value is { Length: var len } && len > maxLength ? value[..maxLength] : value;

    private static NpgsqlParameter _Param(string name, object? value) => new(name, value ?? DBNull.Value);
}
