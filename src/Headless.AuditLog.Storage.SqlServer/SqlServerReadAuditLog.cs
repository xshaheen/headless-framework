// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Serializer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer;

internal sealed class SqlServerReadAuditLog<TContext>(
    IOptions<SqlServerAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions,
    IJsonSerializer serializer
) : IReadAuditLog<TContext>
{
    public async Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(query);
        Argument.IsPositive(query.Limit, "The query limit must be positive.", nameof(query));
        var filters = new List<string>();
        var parameters = new List<SqlParameter> { _Param("Limit", query.Limit) };

        _AddFilter(filters, parameters, "[Action]=@Action", "Action", query.Action);
        _AddFilter(filters, parameters, "[EntityType]=@EntityType", "EntityType", query.EntityType);
        _AddFilter(filters, parameters, "[EntityId]=@EntityId", "EntityId", query.EntityId);
        _AddFilter(filters, parameters, "[UserId]=@UserId", "UserId", query.UserId);
        _AddFilter(filters, parameters, "[TenantId]=@TenantId", "TenantId", query.TenantId);

        if (query.From is not null)
        {
            filters.Add("[CreatedAt]>=@From");
            parameters.Add(_Param("From", query.From.Value.UtcDateTime));
        }

        if (query.To is not null)
        {
            filters.Add("[CreatedAt]<@To");
            parameters.Add(_Param("To", query.To.Value.UtcDateTime));
        }

        var where = filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}";
        var sql =
            $"SELECT TOP(@Limit) [UserId],[AccountId],[TenantId],[IpAddress],[UserAgent],[CorrelationId],[Action],[ChangeType],[EntityType],[EntityId],[OldValues],[NewValues],[ChangedFields],[Success],[ErrorCode],[CreatedAt] FROM {SqlServerAuditLogStorageInitializer.Qualified(storageOptions.Value)}{where} ORDER BY [CreatedAt] DESC, [Id] DESC;";

        var result = new List<AuditLogEntryData>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([.. parameters]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new AuditLogEntryData
                {
                    UserId = await _GetStringAsync(reader, 0, cancellationToken).ConfigureAwait(false),
                    AccountId = await _GetStringAsync(reader, 1, cancellationToken).ConfigureAwait(false),
                    TenantId = await _GetStringAsync(reader, 2, cancellationToken).ConfigureAwait(false),
                    IpAddress = await _GetStringAsync(reader, 3, cancellationToken).ConfigureAwait(false),
                    UserAgent = await _GetStringAsync(reader, 4, cancellationToken).ConfigureAwait(false),
                    CorrelationId = await _GetStringAsync(reader, 5, cancellationToken).ConfigureAwait(false),
                    Action = reader.GetString(6),
                    ChangeType = await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false)
                        ? null
                        : (AuditChangeType)reader.GetInt32(7),
                    EntityType = await _GetStringAsync(reader, 8, cancellationToken).ConfigureAwait(false),
                    EntityId = await _GetStringAsync(reader, 9, cancellationToken).ConfigureAwait(false),
                    OldValues = await _DeserializeAsync<Dictionary<string, object?>>(reader, 10, cancellationToken)
                        .ConfigureAwait(false),
                    NewValues = await _DeserializeAsync<Dictionary<string, object?>>(reader, 11, cancellationToken)
                        .ConfigureAwait(false),
                    ChangedFields = await _DeserializeAsync<List<string>>(reader, 12, cancellationToken)
                        .ConfigureAwait(false),
                    Success = reader.GetBoolean(13),
                    ErrorCode = await _GetStringAsync(reader, 14, cancellationToken).ConfigureAwait(false),
                    CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(15), DateTimeKind.Utc)),
                }
            );
        }

        return result;
    }

    private static void _AddFilter(
        List<string> filters,
        List<SqlParameter> parameters,
        string condition,
        string name,
        string? value
    )
    {
        if (value is null)
        {
            return;
        }

        filters.Add(condition);
        parameters.Add(_Param(name, value));
    }

    private static async Task<string?> _GetStringAsync(
        SqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(ordinal);
    }

    private async Task<T?> _DeserializeAsync<T>(SqlDataReader reader, int ordinal, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return serializer.Deserialize<T>(reader.GetString(ordinal));
    }

    private static SqlParameter _Param(string name, object? value)
    {
        return new($"@{name}", value ?? DBNull.Value);
    }
}
