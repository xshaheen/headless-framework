// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
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
    private const string _GrantColumns =
        "[Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted],[DateCreated],[DateUpdated]";
    private const string _TenantFilter = "(([TenantId] IS NULL AND @TenantId IS NULL) OR [TenantId]=@TenantId)";

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
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sql =
            $"INSERT INTO {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} ([Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted],[DateCreated]) VALUES (@Id,@Name,@ProviderName,@ProviderKey,@TenantId,@IsGranted,@DateCreated);";

        foreach (var permissionGrant in permissionGrants)
        {
            await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
            command.CommandTimeout = _CommandTimeout();
            command.Parameters.AddRange(_Parameters(permissionGrant));
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

    private SqlParameter _TenantParam() => _Param("TenantId", services.GetService<ICurrentTenant>()?.Id);

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

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
        // Preserve caller-supplied DateCreated when present; otherwise stamp from the TimeProvider.
        // Grants are insert-only (revocation deletes then re-inserts), so DateUpdated is never written here.
        var dateCreated =
            permissionGrant.DateCreated == default ? timeProvider.GetUtcNow() : permissionGrant.DateCreated;

        return
        [
            _Param("Id", permissionGrant.Id),
            _Param("Name", permissionGrant.Name),
            _Param("ProviderName", permissionGrant.ProviderName),
            _Param("ProviderKey", permissionGrant.ProviderKey),
            _Param("TenantId", permissionGrant.TenantId),
            _Param("IsGranted", permissionGrant.IsGranted),
            _Param("DateCreated", dateCreated),
        ];
    }

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
