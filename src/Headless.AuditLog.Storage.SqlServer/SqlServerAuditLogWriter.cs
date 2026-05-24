// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.AuditLog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerAuditLogWriter(
    IOptions<SqlServerAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions
)
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    private string? _cachedSql;

    public async Task WriteAsync(IReadOnlyList<AuditLogEntryData> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var sql = _cachedSql ??= _BuildInsertSql();

        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
            command.Parameters.AddRange(_CreateParameters(entry));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True-sync writer using <see cref="SqlCommand.ExecuteNonQuery"/>. Used by the sync
    /// <c>IAuditLogStore.Save</c> path to avoid <c>.GetAwaiter().GetResult()</c> on the async path.
    /// </summary>
    public void WriteSync(IReadOnlyList<AuditLogEntryData> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var sql = _cachedSql ??= _BuildInsertSql();

        using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var entry in entries)
        {
            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddRange(_CreateParameters(entry));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private string _BuildInsertSql() =>
        $"""INSERT INTO {SqlServerAuditLogStorageInitializer.Qualified(storageOptions.Value)} ([CreatedAt],[UserId],[AccountId],[TenantId],[IpAddress],[UserAgent],[CorrelationId],[Action],[ChangeType],[EntityType],[EntityId],[OldValues],[NewValues],[ChangedFields],[Success],[ErrorCode]) VALUES (@CreatedAt,@UserId,@AccountId,@TenantId,@IpAddress,@UserAgent,@CorrelationId,@Action,@ChangeType,@EntityType,@EntityId,@OldValues,@NewValues,@ChangedFields,@Success,@ErrorCode);""";

    private static SqlParameter[] _CreateParameters(AuditLogEntryData entry) =>
    [
        _Param("CreatedAt", entry.CreatedAt.UtcDateTime),
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

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
