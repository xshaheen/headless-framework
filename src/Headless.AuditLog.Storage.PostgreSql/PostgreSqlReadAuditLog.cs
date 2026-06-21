// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.AuditLog.PostgreSql;

internal sealed class PostgreSqlReadAuditLog<TContext>(
    IOptions<PostgreSqlAuditLogOptions> providerOptions,
    IOptions<AuditLogStorageOptions> storageOptions,
    IJsonSerializer serializer
) : IReadAuditLog<TContext>
{
    public async Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(
        string? action = null,
        string? entityType = null,
        string? entityId = null,
        string? userId = null,
        string? tenantId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 100,
        CancellationToken cancellationToken = default
    )
    {
        var filters = new List<string>();
        var parameters = new List<NpgsqlParameter> { _Param("Limit", limit) };

        _AddFilter(filters, parameters, @"""Action""=@Action", "Action", action);
        _AddFilter(filters, parameters, @"""EntityType""=@EntityType", "EntityType", entityType);
        _AddFilter(filters, parameters, @"""EntityId""=@EntityId", "EntityId", entityId);
        _AddFilter(filters, parameters, @"""UserId""=@UserId", "UserId", userId);
        _AddFilter(filters, parameters, @"""TenantId""=@TenantId", "TenantId", tenantId);

        if (from is not null)
        {
            filters.Add(@"""CreatedAt"">=@From");
            parameters.Add(_Param("From", from.Value));
        }

        if (to is not null)
        {
            filters.Add(@"""CreatedAt""<@To");
            parameters.Add(_Param("To", to.Value));
        }

        var where = filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}";
        var sql =
            $"""SELECT "UserId","AccountId","TenantId","IpAddress","UserAgent","CorrelationId","Action","ChangeType","EntityType","EntityId","OldValues","NewValues","ChangedFields","Success","ErrorCode","CreatedAt" FROM {PostgreSqlAuditLogStorageInitializer.Qualified(storageOptions.Value)}{where} ORDER BY "CreatedAt" DESC, "Id" DESC LIMIT @Limit;""";

        var result = new List<AuditLogEntryData>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
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
                    CreatedAt = await reader
                        .GetFieldValueAsync<DateTimeOffset>(15, cancellationToken)
                        .ConfigureAwait(false),
                }
            );
        }

        return result;
    }

    private static void _AddFilter(
        List<string> filters,
        List<NpgsqlParameter> parameters,
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
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    ) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(ordinal);

    private async Task<T?> _DeserializeAsync<T>(
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return serializer.Deserialize<T>(reader.GetString(ordinal));
    }

    private static NpgsqlParameter _Param(string name, object? value) => new(name, value ?? DBNull.Value);
}
