// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Headless.Permissions.Repositories;
using Headless.Primitives;
using Headless.Serializer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Permissions.SqlServer;

internal sealed class SqlServerPermissionDefinitionRecordRepository(
    IOptions<SqlServerPermissionsOptions> providerOptions,
    IOptions<PermissionsStorageOptions> storageOptions,
    IJsonSerializer serializer
) : IPermissionDefinitionRecordRepository
{
    public async Task<List<PermissionDefinitionRecord>> GetPermissionsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT [Id],[GroupName],[Name],[ParentName],[DisplayName],[IsEnabled],[Providers],[ExtraProperties] FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionDefinitionsTableName)};";

        var result = new List<PermissionDefinitionRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new PermissionDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(6)
            );

            foreach (var (key, value) in _DeserializeExtraProperties(reader.GetString(7)))
            {
                record.ExtraProperties[key] = value;
            }

            result.Add(record);
        }

        return result;
    }

    public async Task<List<PermissionGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT [Id],[Name],[DisplayName],[ExtraProperties] FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGroupDefinitionsTableName)};";

        var result = new List<PermissionGroupDefinitionRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new PermissionGroupDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2)
            );

            foreach (var (key, value) in _DeserializeExtraProperties(reader.GetString(3)))
            {
                record.ExtraProperties[key] = value;
            }

            result.Add(record);
        }

        return result;
    }

    public async Task SaveAsync(
        List<PermissionGroupDefinitionRecord> newGroups,
        List<PermissionGroupDefinitionRecord> updatedGroups,
        List<PermissionGroupDefinitionRecord> deletedGroups,
        List<PermissionDefinitionRecord> newPermissions,
        List<PermissionDefinitionRecord> updatedPermissions,
        List<PermissionDefinitionRecord> deletedPermissions,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in newGroups)
        {
            await _ExecuteGroupAsync(
                    connection,
                    (SqlTransaction)transaction,
                    _InsertGroupSql(),
                    record,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var record in updatedGroups)
        {
            await _ExecuteGroupAsync(
                    connection,
                    (SqlTransaction)transaction,
                    _UpdateGroupSql(),
                    record,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var record in deletedGroups)
        {
            await _DeleteAsync(connection, (SqlTransaction)transaction, _DeleteGroupSql(), record.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in newPermissions)
        {
            await _ExecutePermissionAsync(
                    connection,
                    (SqlTransaction)transaction,
                    _InsertPermissionSql(),
                    record,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var record in updatedPermissions)
        {
            await _ExecutePermissionAsync(
                    connection,
                    (SqlTransaction)transaction,
                    _UpdatePermissionSql(),
                    record,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var record in deletedPermissions)
        {
            await _DeleteAsync(
                    connection,
                    (SqlTransaction)transaction,
                    _DeletePermissionSql(),
                    record.Id,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _ExecuteGroupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        PermissionGroupDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@DisplayName", record.DisplayName);
        command.Parameters.AddWithValue(
            "@ExtraProperties",
            serializer.SerializeToString(record.ExtraProperties) ?? "{}"
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _ExecutePermissionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        PermissionDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@GroupName", record.GroupName);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@DisplayName", record.DisplayName);
        command.Parameters.AddWithValue("@IsEnabled", record.IsEnabled);
        command.Parameters.AddWithValue("@ParentName", (object?)record.ParentName ?? DBNull.Value);
        command.Parameters.AddWithValue("@Providers", (object?)record.Providers ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ExtraProperties",
            serializer.SerializeToString(record.ExtraProperties) ?? "{}"
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _DeleteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private int _CommandTimeout()
    {
        return (int)providerOptions.Value.CommandTimeout.TotalSeconds;
    }

    private string _InsertGroupSql()
    {
        return $"INSERT INTO {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGroupDefinitionsTableName)} ([Id],[Name],[DisplayName],[ExtraProperties]) VALUES (@Id,@Name,@DisplayName,@ExtraProperties);";
    }

    private string _UpdateGroupSql()
    {
        return $"UPDATE {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGroupDefinitionsTableName)} SET [Name]=@Name,[DisplayName]=@DisplayName,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;";
    }

    private string _DeleteGroupSql()
    {
        return $"DELETE FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionGroupDefinitionsTableName)} WHERE [Id]=@Id;";
    }

    private string _InsertPermissionSql()
    {
        return $"INSERT INTO {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionDefinitionsTableName)} ([Id],[GroupName],[Name],[DisplayName],[IsEnabled],[ParentName],[Providers],[ExtraProperties]) VALUES (@Id,@GroupName,@Name,@DisplayName,@IsEnabled,@ParentName,@Providers,@ExtraProperties);";
    }

    private string _UpdatePermissionSql()
    {
        return $"UPDATE {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionDefinitionsTableName)} SET [GroupName]=@GroupName,[Name]=@Name,[DisplayName]=@DisplayName,[IsEnabled]=@IsEnabled,[ParentName]=@ParentName,[Providers]=@Providers,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;";
    }

    private string _DeletePermissionSql()
    {
        return $"DELETE FROM {SqlServerPermissionsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.PermissionDefinitionsTableName)} WHERE [Id]=@Id;";
    }

    private ExtraProperties _DeserializeExtraProperties(string json)
    {
        return serializer.Deserialize<ExtraProperties>(json) ?? [];
    }
}
