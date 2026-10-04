// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Primitives;
using Headless.Serializer;
using Headless.Sql;

namespace Headless.Permissions;

/// <summary>
/// The relational <see cref="IPermissionDefinitionRecordRepository"/>, written once over <see cref="ISqlDialect"/>.
/// </summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalPermissionDefinitionRecordRepository(
    RelationalPermissionsTables tables,
    RelationalPermissionsOptions options,
    IJsonSerializer serializer
) : IPermissionDefinitionRecordRepository
{
    // Written text is unbounded: a parameter sized to the column would silently truncate an over-long value, where the
    // column must reject it.
    private static readonly SqlColumnType _Written = SqlColumnType.Text(-1);

    private static readonly string[] _PermissionColumns =
    [
        "Id",
        "GroupName",
        "Name",
        "ParentName",
        "DisplayName",
        "IsEnabled",
        "Providers",
        "ExtraProperties",
    ];

    private static readonly string[] _GroupColumns = ["Id", "Name", "DisplayName", "ExtraProperties"];

    private readonly ISqlDialect _dialect = tables.Dialect;
    private readonly string _selectPermissions = _Select(tables, tables.Definitions, _PermissionColumns);
    private readonly string _insertPermission = _Insert(tables, tables.Definitions, _PermissionColumns);
    private readonly string _updatePermission = _Update(tables, tables.Definitions, _PermissionColumns);
    private readonly string _deletePermission = _Delete(tables, tables.Definitions);
    private readonly string _selectGroups = _Select(tables, tables.Groups, _GroupColumns);
    private readonly string _insertGroup = _Insert(tables, tables.Groups, _GroupColumns);
    private readonly string _updateGroup = _Update(tables, tables.Groups, _GroupColumns);
    private readonly string _deleteGroup = _Delete(tables, tables.Groups);

    public async Task<List<PermissionDefinitionRecord>> GetPermissionsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = new List<PermissionDefinitionRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, _selectPermissions);
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

            _CopyExtraProperties(reader.GetString(7), record.ExtraProperties);
            result.Add(record);
        }

        return result;
    }

    public async Task<List<PermissionGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = new List<PermissionGroupDefinitionRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, _selectGroups);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new PermissionGroupDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2)
            );
            _CopyExtraProperties(reader.GetString(3), record.ExtraProperties);
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
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in newGroups)
        {
            await _WriteGroupAsync(connection, transaction, _insertGroup, record, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in updatedGroups)
        {
            await _WriteGroupAsync(connection, transaction, _updateGroup, record, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in deletedGroups)
        {
            await _DeleteAsync(connection, transaction, _deleteGroup, record.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in newPermissions)
        {
            await _WritePermissionAsync(connection, transaction, _insertPermission, record, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in updatedPermissions)
        {
            await _WritePermissionAsync(connection, transaction, _updatePermission, record, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in deletedPermissions)
        {
            await _DeleteAsync(connection, transaction, _deletePermission, record.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _WriteGroupAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        PermissionGroupDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, transaction, sql);
        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, record.Id);
        _dialect.AddParameter(command, "Name", _Written, record.Name);
        _dialect.AddParameter(command, "DisplayName", _Written, record.DisplayName);
        _AddExtraProperties(command, record.ExtraProperties);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _WritePermissionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        PermissionDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, transaction, sql);
        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, record.Id);
        _dialect.AddParameter(command, "GroupName", _Written, record.GroupName);
        _dialect.AddParameter(command, "Name", _Written, record.Name);
        _dialect.AddParameter(command, "ParentName", _Written, record.ParentName);
        _dialect.AddParameter(command, "DisplayName", _Written, record.DisplayName);
        _dialect.AddParameter(command, "IsEnabled", SqlColumnType.Boolean, record.IsEnabled);
        _dialect.AddParameter(command, "Providers", _Written, record.Providers);
        _AddExtraProperties(command, record.ExtraProperties);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _DeleteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, transaction, sql);
        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _AddExtraProperties(DbCommand command, ExtraProperties extraProperties)
    {
        _dialect.AddParameter(
            command,
            "ExtraProperties",
            _Written,
            serializer.SerializeToString(extraProperties) ?? "{}"
        );
    }

    private void _CopyExtraProperties(string json, ExtraProperties target)
    {
        foreach (var (key, value) in serializer.Deserialize<ExtraProperties>(json) ?? [])
        {
            target[key] = value;
        }
    }

    private DbCommand _CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        return command;
    }

    private static string _Select(RelationalPermissionsTables t, string table, string[] columns)
    {
        return $"SELECT {string.Join(", ", columns.Select(t.Column))} FROM {table};";
    }

    private static string _Insert(RelationalPermissionsTables t, string table, string[] columns)
    {
        return $"INSERT INTO {table} ({string.Join(", ", columns.Select(t.Column))}) "
            + $"VALUES ({string.Join(", ", columns.Select(static c => "@" + c))});";
    }

    private static string _Update(RelationalPermissionsTables t, string table, string[] columns)
    {
        return $"UPDATE {table} SET {string.Join(", ", columns.Skip(1).Select(c => $"{t.Column(c)} = @{c}"))} "
            + $"WHERE {t.Column("Id")} = @Id;";
    }

    private static string _Delete(RelationalPermissionsTables t, string table)
    {
        return $"DELETE FROM {table} WHERE {t.Column("Id")} = @Id;";
    }
}
#pragma warning restore CA2100
