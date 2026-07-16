// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text;
using Headless.Abstractions;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.SqlServer;

internal sealed class SqlServerPermissionGrantRepository(
    IOptions<SqlServerPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions,
    IServiceProvider services,
    TimeProvider timeProvider
) : IPermissionGrantRepository
{
    // 100 rows use 700 parameters, safely below SQL Server's 2,100-parameter ceiling.
    private const int _MaxRowsPerInsert = 100;
    private const string _GrantColumns =
        "[Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted],[DateCreated],[DateUpdated]";
    private const string _TenantFilter = "(([TenantId] IS NULL AND @TenantId IS NULL) OR [TenantId]=@TenantId)";

    private readonly ConcurrentDictionary<int, string> _insertBatchSql = new();

    public async Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT TOP(1) {_GrantColumns} FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [Name]=@Name AND [ProviderName]=@ProviderName AND [ProviderKey]=@ProviderKey AND {_TenantFilter} ORDER BY [Id];";

        return
            await _ReadAsync(
                    sql,
                    cancellationToken,
                    _Param("Name", name),
                    _Param("ProviderName", providerName),
                    _Param("ProviderKey", providerKey),
                    _TenantParam()
                )
                .ConfigureAwait(false)
                is [var row, ..]
            ? row
            : null;
    }

    public Task<List<PermissionGrantRecord>> GetListAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT {_GrantColumns} FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [ProviderName]=@ProviderName AND [ProviderKey]=@ProviderKey AND {_TenantFilter};";

        return _ReadAsync(
            sql,
            cancellationToken,
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey),
            _TenantParam()
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

        // Pass the names through the HeadlessPermissionsNameList TVP: one cached plan regardless of count
        // and no 2100-parameter ceiling, portable to older engines (no OPENJSON / compatibility level 130).
        var sql =
            $"SELECT {_GrantColumns} FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [Name] IN (SELECT [Name] FROM @Names) AND [ProviderName]=@ProviderName AND [ProviderKey]=@ProviderKey AND {_TenantFilter};";

        return _ReadAsync(
            sql,
            cancellationToken,
            _BuildNameListTvpParameter(names),
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey),
            _TenantParam()
        );
    }

    public Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        var sql =
            $"INSERT INTO {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} ([Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted],[DateCreated]) VALUES (@Id,@Name,@ProviderName,@ProviderKey,@TenantId,@IsGranted,@DateCreated);";

        return _ExecuteAsync(sql, cancellationToken, _Parameters(permissionGrant));
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

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        for (var offset = 0; offset < records.Count; offset += _MaxRowsPerInsert)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowCount = Math.Min(_MaxRowsPerInsert, records.Count - offset);
            var sql = _insertBatchSql.GetOrAdd(rowCount, _BuildInsertSql);
            await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
            command.CommandTimeout = _CommandTimeout();

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                _AddBatchParameters(command.Parameters, records[offset + rowIndex], rowIndex);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        var sql =
            $"DELETE FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [Id]=@Id AND {_TenantFilter};";

        await _ExecuteAsync(sql, cancellationToken, _Param("Id", permissionGrant.Id), _TenantParam())
            .ConfigureAwait(false);
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

        // Pass ids through the HeadlessPermissionsIdList TVP: one cached plan regardless of count, no
        // 2100-parameter ceiling, portable to older engines (no OPENJSON / compatibility level 130).
        var sql =
            $"DELETE FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [Id] IN (SELECT [Id] FROM @Ids) AND {_TenantFilter};";

        await _ExecuteAsync(
                sql,
                cancellationToken,
                _BuildIdListTvpParameter(permissionGrants.Select(permissionGrant => permissionGrant.Id)),
                _TenantParam()
            )
            .ConfigureAwait(false);
    }

    private async Task<List<PermissionGrantRecord>> _ReadAsync(
        string sql,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters
    )
    {
        var result = new List<PermissionGrantRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
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

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqlParameter _TenantParam()
    {
        return _Param("TenantId", services.GetService<ICurrentTenant>()?.Id);
    }

    private int _CommandTimeout()
    {
        return (int)providerOptions.Value.CommandTimeout.TotalSeconds;
    }

    private SqlParameter _BuildIdListTvpParameter(IEnumerable<Guid> ids)
    {
        var idsTable = new DataTable();
        idsTable.Columns.Add("Id", typeof(Guid));
        foreach (var id in ids)
        {
            idsTable.Rows.Add(id);
        }

        return new SqlParameter("@Ids", SqlDbType.Structured)
        {
            TypeName = $"[{storageOptions.Value.Schema}].[HeadlessPermissionsIdList]",
            Value = idsTable,
        };
    }

    private SqlParameter _BuildNameListTvpParameter(IEnumerable<string> names)
    {
        var namesTable = new DataTable();
        namesTable.Columns.Add("Name", typeof(string));
        foreach (var name in names)
        {
            namesTable.Rows.Add(name);
        }

        return new SqlParameter("@Names", SqlDbType.Structured)
        {
            TypeName = $"[{storageOptions.Value.Schema}].[HeadlessPermissionsNameList]",
            Value = namesTable,
        };
    }

    private SqlParameter[] _Parameters(PermissionGrantRecord permissionGrant)
    {
        return
        [
            _Param("Id", permissionGrant.Id),
            _Param("Name", permissionGrant.Name),
            _Param("ProviderName", permissionGrant.ProviderName),
            _Param("ProviderKey", permissionGrant.ProviderKey),
            _Param("TenantId", permissionGrant.TenantId),
            _Param("IsGranted", permissionGrant.IsGranted),
            _Param("DateCreated", _DateCreated(permissionGrant)),
        ];
    }

    private void _AddBatchParameters(
        SqlParameterCollection parameters,
        PermissionGrantRecord permissionGrant,
        int rowIndex
    )
    {
        parameters.Add(_Param(_ParameterName("Id", rowIndex), permissionGrant.Id));
        parameters.Add(_Param(_ParameterName("Name", rowIndex), permissionGrant.Name));
        parameters.Add(_Param(_ParameterName("ProviderName", rowIndex), permissionGrant.ProviderName));
        parameters.Add(_Param(_ParameterName("ProviderKey", rowIndex), permissionGrant.ProviderKey));
        parameters.Add(_Param(_ParameterName("TenantId", rowIndex), permissionGrant.TenantId));
        parameters.Add(_Param(_ParameterName("IsGranted", rowIndex), permissionGrant.IsGranted));
        parameters.Add(_Param(_ParameterName("DateCreated", rowIndex), _DateCreated(permissionGrant)));
    }

    private string _BuildInsertSql(int rowCount)
    {
        var builder = new StringBuilder(192 + (rowCount * 144));
        builder.Append("INSERT INTO ");
        builder.Append(
            SqlServerPermissionsStorageInitializer.Qualified(
                storageOptions.Value,
                storageOptions.Value.PermissionGrantsTableName
            )
        );
        builder.Append(" ([Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted],[DateCreated]) VALUES ");

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            if (rowIndex > 0)
            {
                builder.Append(',');
            }

            builder.Append(
                CultureInfo.InvariantCulture,
                $"(@Id_{rowIndex},@Name_{rowIndex},@ProviderName_{rowIndex},@ProviderKey_{rowIndex},@TenantId_{rowIndex},@IsGranted_{rowIndex},@DateCreated_{rowIndex})"
            );
        }

        return builder.Append(';').ToString();
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

    private DateTimeOffset _DateCreated(PermissionGrantRecord permissionGrant)
    {
        // Preserve caller-supplied DateCreated when present; otherwise stamp from the TimeProvider.
        // Grants are insert-only (revocation deletes then re-inserts), so DateUpdated is never written here.
        return permissionGrant.DateCreated == default ? timeProvider.GetUtcNow() : permissionGrant.DateCreated;
    }

    private static string _ParameterName(string name, int rowIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{name}_{rowIndex}");
    }

    private static SqlParameter _Param(string name, object? value)
    {
        return new($"@{name}", value ?? DBNull.Value);
    }
}
