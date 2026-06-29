// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable CA2100 // SQL text only interpolates validated schema/table identifiers; values remain parameterized.
namespace Headless.Permissions.PostgreSql;

internal sealed class PostgreSqlPermissionGrantRepository(
    IOptions<PostgreSqlPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions,
    IServiceProvider services
) : IPermissionGrantRepository
{
    private const string _GrantColumns = @"""Id"",""Name"",""ProviderName"",""ProviderKey"",""TenantId"",""IsGranted""";
    private const string _TenantFilter = @"""TenantId"" IS NOT DISTINCT FROM @TenantId";

    public async Task<PermissionGrantRecord?> FindAsync(
        string name,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT {_GrantColumns} FROM {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE "Name"=@Name AND "ProviderName"=@ProviderName AND "ProviderKey"=@ProviderKey AND {_TenantFilter} ORDER BY "Id" LIMIT 1;""";

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
            $"""SELECT {_GrantColumns} FROM {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE "ProviderName"=@ProviderName AND "ProviderKey"=@ProviderKey AND {_TenantFilter};""";

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

        var sql =
            $"""SELECT {_GrantColumns} FROM {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE "Name" = ANY(@Names) AND "ProviderName"=@ProviderName AND "ProviderKey"=@ProviderKey AND {_TenantFilter};""";

        return _ReadAsync(
            sql,
            cancellationToken,
            _Param("Names", names.ToArray()),
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey),
            _TenantParam()
        );
    }

    public Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""INSERT INTO {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} ("Id","Name","ProviderName","ProviderKey","TenantId","IsGranted") VALUES (@Id,@Name,@ProviderName,@ProviderKey,@TenantId,@IsGranted);""";

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
            $"""INSERT INTO {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} ("Id","Name","ProviderName","ProviderKey","TenantId","IsGranted") VALUES (@Id,@Name,@ProviderName,@ProviderKey,@TenantId,@IsGranted);""";

        foreach (var permissionGrant in permissionGrants)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.CommandTimeout = _CommandTimeout();
            command.Parameters.AddRange(_Parameters(permissionGrant));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        var sql =
            $"""DELETE FROM {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE "Id"=@Id AND {_TenantFilter};""";

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

        var sql =
            $"""DELETE FROM {PostgreSqlPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE "Id" = ANY(@Ids) AND {_TenantFilter};""";

        await _ExecuteAsync(
                sql,
                cancellationToken,
                _Param("Ids", permissionGrants.Select(x => x.Id).ToArray()),
                _TenantParam()
            )
            .ConfigureAwait(false);
    }

    private async Task<List<PermissionGrantRecord>> _ReadAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters
    )
    {
        var result = new List<PermissionGrantRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new PermissionGrantRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(5),
                    await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4)
                )
            );
        }

        return result;
    }

    private async Task _ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private NpgsqlParameter _TenantParam() => _Param("TenantId", services.GetService<ICurrentTenant>()?.Id);

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    private static NpgsqlParameter[] _Parameters(PermissionGrantRecord permissionGrant) =>
        [
            _Param("Id", permissionGrant.Id),
            _Param("Name", permissionGrant.Name),
            _Param("ProviderName", permissionGrant.ProviderName),
            _Param("ProviderKey", permissionGrant.ProviderKey),
            _Param("TenantId", permissionGrant.TenantId),
            _Param("IsGranted", permissionGrant.IsGranted),
        ];

    private static NpgsqlParameter _Param(string name, object? value) => new(name, value ?? DBNull.Value);
}
