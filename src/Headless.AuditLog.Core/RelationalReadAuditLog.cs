// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Primitives;
using Headless.Serializer;
using Headless.Sql;

namespace Headless.AuditLog;

/// <summary>The relational <see cref="IReadAuditLog{TContext}"/>: keyset pages over (<c>CreatedAt</c>, <c>Id</c>).</summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalReadAuditLog<TContext>(RelationalAuditLogTable table, IJsonSerializer serializer)
    : IReadAuditLog<TContext>
{
    private static readonly string[] _Columns =
    [
        "Id",
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
        "CreatedAt",
    ];

    private readonly ISqlDialect _dialect = table.Dialect;
    private readonly string _createdAt = table.Column("CreatedAt");
    private readonly string _id = table.Column("Id");
    private readonly string _select =
        $"SELECT {string.Join(", ", _Columns.Select(table.Column))} FROM {table.Qualified}";

    public async Task<ContinuationPage<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var position = AuditLogPaging.Validate(query);
        var filters = new List<string>();
        var binders = new List<Action<DbCommand>>();

        _AddFilter(filters, binders, "Action", query.Action, AuditLogFieldLimits.Action);
        _AddFilter(filters, binders, "EntityType", query.EntityType, AuditLogFieldLimits.EntityType);
        _AddFilter(filters, binders, "EntityId", query.EntityId, AuditLogFieldLimits.EntityId);
        _AddFilter(filters, binders, "UserId", query.UserId, AuditLogFieldLimits.UserId);
        _AddFilter(filters, binders, "AccountId", query.AccountId, AuditLogFieldLimits.AccountId);
        _AddFilter(filters, binders, "TenantId", query.TenantId, AuditLogFieldLimits.TenantId);
        _AddFilter(filters, binders, "CorrelationId", query.CorrelationId, AuditLogFieldLimits.CorrelationId);

        if (query.From is { } from)
        {
            filters.Add($"{_createdAt} >= @From");
            binders.Add(command => table.BindCreatedAt(command, "From", from));
        }

        if (query.To is { } to)
        {
            filters.Add($"{_createdAt} < @To");
            binders.Add(command => table.BindCreatedAt(command, "To", to));
        }

        var newestFirst = query.Direction == AuditLogSortDirection.NewestFirst;

        if (position is { } after)
        {
            // The keyset predicate (CreatedAt, Id) past the position, with its CreatedAt bound stated on its own so
            // both engines seek the (…, CreatedAt, Id) indexes to the position instead of filtering from the start.
            var (bound, strict) = newestFirst ? ("<=", "<") : (">=", ">");
            filters.Add(
                $"{_createdAt} {bound} @AfterCreatedAt "
                    + $"AND ({_createdAt} {strict} @AfterCreatedAt OR {_id} {strict} @AfterId)"
            );
            binders.Add(command =>
            {
                table.BindCreatedAt(
                    command,
                    "AfterCreatedAt",
                    new DateTimeOffset(DateTime.SpecifyKind(after.CreatedAtUtc, DateTimeKind.Utc))
                );
                _dialect.AddParameter(command, "AfterId", SqlColumnType.Int64, after.Id);
            });
        }

        var direction = newestFirst ? "DESC" : "ASC";
        var where = filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}";
        // OFFSET … FETCH is the row limit both engines accept; one extra row tells whether another page exists
        // without a second count query.
        var sql =
            $"{_select}{where} ORDER BY {_createdAt} {direction}, {_id} {direction} "
            + "OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;";

        var result = new List<AuditLogEntryData>();
        (DateTime CreatedAtUtc, long Id) last = default;
        await using var connection = _dialect.CreateConnection(table.Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = table.Options.CommandTimeoutSeconds;
        _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, query.Size + 1);

        foreach (var bind in binders)
        {
            bind(command);
        }

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

            var createdAt = RelationalAuditLogTable.ReadCreatedAt(reader, 16);
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

    private void _AddFilter(
        List<string> filters,
        List<Action<DbCommand>> binders,
        string column,
        string? value,
        int maxLength
    )
    {
        if (value is null)
        {
            return;
        }

        filters.Add($"{table.Column(column)} = @{column}");
        binders.Add(command => _dialect.AddParameter(command, column, _Filter(maxLength, value), value));
    }

    // Sized to the column, so the plan is reused and the comparison keeps the column's collation. A longer value is
    // bound unsized: a sized SQL Server parameter would truncate it and match the rows that store its prefix.
    private static SqlColumnType _Filter(int maxLength, string value)
    {
        return SqlColumnType.Text(value.Length > maxLength ? -1 : maxLength);
    }

    private static async Task<string?> _GetStringAsync(
        DbDataReader reader,
        int ordinal,
        CancellationToken cancellationToken
    )
    {
        return await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
            ? null
            : reader.GetString(ordinal);
    }

    private async Task<T?> _DeserializeAsync<T>(DbDataReader reader, int ordinal, CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return serializer.Deserialize<T>(reader.GetString(ordinal));
    }
}
#pragma warning restore CA2100
