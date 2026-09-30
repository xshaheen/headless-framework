// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
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
    public async Task<ContinuationPage<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var position = AuditLogPaging.Validate(query);
        var filters = new List<string>();
        // One extra row tells whether another page exists without a second count query.
        var parameters = new List<NpgsqlParameter> { _Param("Limit", query.Size + 1) };

        _AddFilter(filters, parameters, @"""action""=@Action", "Action", query.Action);
        _AddFilter(filters, parameters, @"""entity_type""=@EntityType", "EntityType", query.EntityType);
        _AddFilter(filters, parameters, @"""entity_id""=@EntityId", "EntityId", query.EntityId);
        _AddFilter(filters, parameters, @"""user_id""=@UserId", "UserId", query.UserId);
        _AddFilter(filters, parameters, @"""account_id""=@AccountId", "AccountId", query.AccountId);
        _AddFilter(filters, parameters, @"""tenant_id""=@TenantId", "TenantId", query.TenantId);
        _AddFilter(filters, parameters, @"""correlation_id""=@CorrelationId", "CorrelationId", query.CorrelationId);

        if (query.From is not null)
        {
            filters.Add(@"""created_at"">=@From");
            parameters.Add(_Param("From", query.From.Value));
        }

        if (query.To is not null)
        {
            filters.Add(@"""created_at""<@To");
            parameters.Add(_Param("To", query.To.Value));
        }

        var newestFirst = query.Direction == AuditLogSortDirection.NewestFirst;

        if (position is { } after)
        {
            // A row-value comparison lets PostgreSQL seek the (…, created_at, id) index to the continuation position.
            filters.Add(
                newestFirst
                    ? @"(""created_at"",""id"")<(@AfterCreatedAt,@AfterId)"
                    : @"(""created_at"",""id"")>(@AfterCreatedAt,@AfterId)"
            );
            parameters.Add(_Param("AfterCreatedAt", new DateTimeOffset(after.CreatedAtUtc, TimeSpan.Zero)));
            parameters.Add(_Param("AfterId", after.Id));
        }

        var order = newestFirst ? @"""created_at"" DESC, ""id"" DESC" : @"""created_at"" ASC, ""id"" ASC";
        var where = filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}";
        var sql =
            $"""SELECT "id","user_id","account_id","tenant_id","ip_address","user_agent","correlation_id","action","change_type","entity_type","entity_id","old_values","new_values","changed_fields","success","error_code","created_at" FROM {PostgreSqlAuditLogSchema.Qualified(storageOptions.Value)}{where} ORDER BY {order} LIMIT @Limit;""";

        var result = new List<AuditLogEntryData>();
        (DateTime CreatedAtUtc, long Id) last = default;
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (result.Count == query.Size)
            {
                // The extra row only signals that another page exists; the token points at the last row kept.
                return new ContinuationPage<AuditLogEntryData>(
                    result,
                    query.Size,
                    AuditLogPaging.Encode(last.CreatedAtUtc, last.Id)
                );
            }

            var createdAt = await reader
                .GetFieldValueAsync<DateTimeOffset>(16, cancellationToken)
                .ConfigureAwait(false);
            last = (createdAt.UtcDateTime, reader.GetInt64(0));
            result.Add(
                new AuditLogEntryData
                {
                    UserId = await _GetStringAsync(reader, 1, cancellationToken).ConfigureAwait(false),
                    AccountId = await _GetStringAsync(reader, 2, cancellationToken).ConfigureAwait(false),
                    TenantId = await _GetStringAsync(reader, 3, cancellationToken).ConfigureAwait(false),
                    IpAddress = await _GetStringAsync(reader, 4, cancellationToken).ConfigureAwait(false),
                    UserAgent = await _GetStringAsync(reader, 5, cancellationToken).ConfigureAwait(false),
                    CorrelationId = await _GetStringAsync(reader, 6, cancellationToken).ConfigureAwait(false),
                    Action = reader.GetString(7),
                    ChangeType = await reader.IsDBNullAsync(8, cancellationToken).ConfigureAwait(false)
                        ? null
                        : (AuditChangeType)reader.GetInt32(8),
                    EntityType = await _GetStringAsync(reader, 9, cancellationToken).ConfigureAwait(false),
                    EntityId = await _GetStringAsync(reader, 10, cancellationToken).ConfigureAwait(false),
                    OldValues = await _DeserializeAsync<Dictionary<string, object?>>(reader, 11, cancellationToken)
                        .ConfigureAwait(false),
                    NewValues = await _DeserializeAsync<Dictionary<string, object?>>(reader, 12, cancellationToken)
                        .ConfigureAwait(false),
                    ChangedFields = await _DeserializeAsync<List<string>>(reader, 13, cancellationToken)
                        .ConfigureAwait(false),
                    Success = reader.GetBoolean(14),
                    ErrorCode = await _GetStringAsync(reader, 15, cancellationToken).ConfigureAwait(false),
                    CreatedAt = createdAt,
                }
            );
        }

        return new ContinuationPage<AuditLogEntryData>(result, query.Size, continuationToken: null);
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
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(ordinal);
    }

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

    private static NpgsqlParameter _Param(string name, object? value)
    {
        return new(name, value ?? DBNull.Value);
    }
}
