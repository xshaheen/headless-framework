// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.SqlServer;

internal sealed class SqlServerPermissionGrantRepository(
    IOptions<SqlServerPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions,
    IServiceProvider services
) : IPermissionGrantRepository
{
    private const int _MaxDeleteParameters = 2000;
    private const int _MaxNameParameters = 2000;
    private const string _GrantColumns = "[Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted]";
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

    public async Task<List<PermissionGrantRecord>> GetListAsync(
        IReadOnlyCollection<string> names,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Count == 0)
        {
            return [];
        }

        var result = new List<PermissionGrantRecord>();

        foreach (var chunk in names.Chunk(_MaxNameParameters))
        {
            var nameParameters = chunk.Select((_, index) => $"@Name{index}").ToArray();
            var parameters = chunk.Select((name, index) => _Param($"Name{index}", name)).ToList();
            parameters.Add(_Param("ProviderName", providerName));
            parameters.Add(_Param("ProviderKey", providerKey));
            parameters.Add(_TenantParam());

            var sql =
                $"SELECT {_GrantColumns} FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [Name] IN ({string.Join(",", nameParameters)}) AND [ProviderName]=@ProviderName AND [ProviderKey]=@ProviderKey AND {_TenantFilter};";

            result.AddRange(await _ReadAsync(sql, cancellationToken, parameters.ToArray()).ConfigureAwait(false));
        }

        return result;
    }

    public Task InsertAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken = default)
    {
        var sql =
            $"INSERT INTO {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} ([Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted]) VALUES (@Id,@Name,@ProviderName,@ProviderKey,@TenantId,@IsGranted);";

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
            $"INSERT INTO {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} ([Id],[Name],[ProviderName],[ProviderKey],[TenantId],[IsGranted]) VALUES (@Id,@Name,@ProviderName,@ProviderKey,@TenantId,@IsGranted);";

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
        await _PublishAsync(permissionGrant, cancellationToken).ConfigureAwait(false);
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

        foreach (var chunk in permissionGrants.Chunk(_MaxDeleteParameters))
        {
            var idParameters = chunk.Select((_, index) => $"@Id{index}").ToArray();
            var parameters = chunk
                .Select((permissionGrant, index) => _Param($"Id{index}", permissionGrant.Id))
                .ToList();
            parameters.Add(_TenantParam());
            var sql =
                $"DELETE FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGrantsTableName)} WHERE [Id] IN ({string.Join(",", idParameters)}) AND {_TenantFilter};";

            await _ExecuteAsync(sql, cancellationToken, parameters.ToArray()).ConfigureAwait(false);
        }

        foreach (var permissionGrant in permissionGrants)
        {
            await _PublishAsync(permissionGrant, cancellationToken).ConfigureAwait(false);
        }
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

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _PublishAsync(PermissionGrantRecord permissionGrant, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetService<ILocalMessagePublisher>();

        if (publisher is not null)
        {
            await publisher
                .PublishAsync(new EntityChangedEventData<PermissionGrantRecord>(permissionGrant), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private SqlParameter _TenantParam() => _Param("TenantId", services.GetService<ICurrentTenant>()?.Id);

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    private static SqlParameter[] _Parameters(PermissionGrantRecord permissionGrant) =>
        [
            _Param("Id", permissionGrant.Id),
            _Param("Name", permissionGrant.Name),
            _Param("ProviderName", permissionGrant.ProviderName),
            _Param("ProviderKey", permissionGrant.ProviderKey),
            _Param("TenantId", permissionGrant.TenantId),
            _Param("IsGranted", permissionGrant.IsGranted),
        ];

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
