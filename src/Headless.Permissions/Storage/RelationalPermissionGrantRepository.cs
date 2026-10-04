// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Headless.MultiTenancy;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

/// <summary>
/// The relational <see cref="IPermissionGrantRepository"/>, written once over <see cref="ISqlDialect"/>. Reads and
/// deletes are scoped to the current tenant; list filters bind the whole list as one parameter.
/// </summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalPermissionGrantRepository(
    RelationalPermissionsTables tables,
    RelationalPermissionsOptions options,
    IServiceProvider services,
    TimeProvider timeProvider
) : IPermissionGrantRepository
{
    // Keeps statements bounded for predictable parse and lock duration: 100 rows use 700 parameters, well below
    // SQL Server's 2,100-parameter ceiling.
    private const int _MaxRowsPerInsert = 100;

    // Written text is unbounded: a parameter sized to the column would silently truncate an over-long value, where the
    // column must reject it.
    private static readonly SqlColumnType _Written = SqlColumnType.Text(-1);

    private static readonly string[] _InsertColumns =
    [
        "Id",
        "Name",
        "ProviderName",
        "ProviderKey",
        "TenantId",
        "IsGranted",
        "CreatedAt",
    ];

    private readonly ISqlDialect _dialect = tables.Dialect;
    private readonly string _select = _BuildSelect(tables);
    private readonly string _byTenant = _BuildByTenant(tables);

    private readonly string _byScope =
        $"{tables.Column("ProviderName")} = @ProviderName AND {tables.Column("ProviderKey")} = @ProviderKey";

    private readonly string _id = tables.Column("Id");
    private readonly string _name = tables.Column("Name");
    private readonly ConcurrentDictionary<int, string> _insertSql = new();

    public async Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        // The unique indexes allow one row per scope; the order picks the same one if a schema lacks them.
        var sql = $"{_select} WHERE {_name} = @Name AND {_byScope} AND {_byTenant} ORDER BY {_id};";

        var rows = await _ReadAsync(
                sql,
                command =>
                {
                    _AddFilter(command, "Name", PermissionGrantRecordConstants.NameMaxLength, name);
                    _AddScope(command, providerName, providerKey);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return rows is [var row, ..] ? row : null;
    }

    public Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return _ReadAsync(
            $"{_select} WHERE {_byScope} AND {_byTenant};",
            command => _AddScope(command, providerName, providerKey),
            cancellationToken
        );
    }

    public Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Count == 0)
        {
            return Task.FromResult(new List<PermissionGrantRecord>());
        }

        var nameList = SqlColumnType.LookupText(PermissionGrantRecordConstants.NameMaxLength, names);

        var sql = $"{_select} WHERE {_dialect.InList(_name, "Names", nameList)} AND {_byScope} AND {_byTenant};";

        return _ReadAsync(
            sql,
            command =>
            {
                _dialect.AddListParameter(command, "Names", nameList, names);
                _AddScope(command, providerName, providerKey);
            },
            cancellationToken
        );
    }

    public Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        return InsertManyAsync([permissionGrant], cancellationToken);
    }

    public async Task InsertManyAsync(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        var records = _Materialize(permissionGrants, cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        for (var offset = 0; offset < records.Count; offset += _MaxRowsPerInsert)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowCount = Math.Min(_MaxRowsPerInsert, records.Count - offset);
            await using var command = _CreateCommand(
                connection,
                transaction,
                _insertSql.GetOrAdd(rowCount, _BuildInsertSql)
            );

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                _AddInsertParameters(command, records[offset + rowIndex], rowIndex);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        return DeleteManyAsync([permissionGrant], cancellationToken);
    }

    public async Task DeleteManyAsync(
        IReadOnlyCollection<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken = default
    )
    {
        if (permissionGrants.Count == 0)
        {
            return;
        }

        var sql =
            $"DELETE FROM {tables.Grants} WHERE {_dialect.InList(_id, "Ids", SqlColumnType.Guid)} AND {_byTenant};";

        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, sql);
        _dialect.AddListParameter(command, "Ids", SqlColumnType.Guid, permissionGrants.Select(x => x.Id).ToList());
        _AddTenant(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _AddScope(DbCommand command, string providerName, string providerKey)
    {
        _AddFilter(command, "ProviderName", PermissionGrantRecordConstants.ProviderNameMaxLength, providerName);
        _AddFilter(command, "ProviderKey", PermissionGrantRecordConstants.ProviderKeyMaxLength, providerKey);
        _AddTenant(command);
    }

    private void _AddTenant(DbCommand command)
    {
        _AddFilter(
            command,
            "TenantId",
            PermissionGrantRecordConstants.TenantIdMaxLength,
            services.GetService<ICurrentTenant>()?.Id
        );
    }

    private void _AddInsertParameters(DbCommand command, PermissionGrantRecord permissionGrant, int rowIndex)
    {
        _dialect.AddParameter(command, _ParameterName("Id", rowIndex), SqlColumnType.Guid, permissionGrant.Id);
        _dialect.AddParameter(command, _ParameterName("Name", rowIndex), _Written, permissionGrant.Name);
        _dialect.AddParameter(
            command,
            _ParameterName("ProviderName", rowIndex),
            _Written,
            permissionGrant.ProviderName
        );
        _dialect.AddParameter(command, _ParameterName("ProviderKey", rowIndex), _Written, permissionGrant.ProviderKey);
        _dialect.AddParameter(command, _ParameterName("TenantId", rowIndex), _Written, permissionGrant.TenantId);
        _dialect.AddParameter(
            command,
            _ParameterName("IsGranted", rowIndex),
            SqlColumnType.Boolean,
            permissionGrant.IsGranted
        );
        // A caller-supplied CreatedAt is kept; only a default one is stamped now. Grants are insert-only (revocation
        // deletes then re-inserts), so UpdatedAt is never written here.
        _dialect.AddParameter(
            command,
            _ParameterName("CreatedAt", rowIndex),
            SqlColumnType.Timestamp,
            permissionGrant.CreatedAt == default ? timeProvider.GetUtcNow() : permissionGrant.CreatedAt
        );
    }

    private async Task<List<PermissionGrantRecord>> _ReadAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken
    )
    {
        var result = new List<PermissionGrantRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, sql);
        bind(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                PermissionGrantRecord.FromStorage(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(5),
                    await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                    await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken).ConfigureAwait(false),
                    await reader.IsDBNullAsync(7, cancellationToken).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false)
                )
            );
        }

        return result;
    }

    private void _AddFilter(DbCommand command, string parameter, int maxLength, string? value)
    {
        _dialect.AddParameter(command, parameter, SqlColumnType.LookupText(maxLength, value), value);
    }

    private DbCommand _CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        return command;
    }

    private string _BuildInsertSql(int rowCount)
    {
        var builder = new StringBuilder(192 + (rowCount * 144))
            .Append("INSERT INTO ")
            .Append(tables.Grants)
            .Append(" (")
            .AppendJoin(", ", _InsertColumns.Select(tables.Column))
            .Append(") VALUES ");

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            builder
                .Append(rowIndex > 0 ? ", (" : "(")
                .AppendJoin(", ", _InsertColumns.Select(column => "@" + _ParameterName(column, rowIndex)))
                .Append(')');
        }

        return builder.Append(';').ToString();
    }

    private static string _BuildSelect(RelationalPermissionsTables t)
    {
        string[] columns =
        [
            "Id",
            "Name",
            "ProviderName",
            "ProviderKey",
            "TenantId",
            "IsGranted",
            "CreatedAt",
            "UpdatedAt",
        ];

        return $"SELECT {string.Join(", ", columns.Select(t.Column))} FROM {t.Grants}";
    }

    // Null-safe on both engines: a null tenant matches only the host's rows.
    private static string _BuildByTenant(RelationalPermissionsTables t)
    {
        var tenantId = t.Column("TenantId");

        return $"(({tenantId} IS NULL AND @TenantId IS NULL) OR {tenantId} = @TenantId)";
    }

    private static List<PermissionGrantRecord> _Materialize(
        IEnumerable<PermissionGrantRecord> permissionGrants,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = permissionGrants.TryGetNonEnumeratedCount(out var count)
            ? new List<PermissionGrantRecord>(count)
            : [];

        foreach (var permissionGrant in permissionGrants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records.Add(permissionGrant);
        }

        return records;
    }

    private static string _ParameterName(string name, int rowIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{name}_{rowIndex}");
    }
}
#pragma warning restore CA2100
