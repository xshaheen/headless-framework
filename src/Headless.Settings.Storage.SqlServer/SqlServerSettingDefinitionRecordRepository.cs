// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Headless.Serializer;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="ISettingDefinitionRecordRepository"/> that stores
/// setting definition records directly via <c>Microsoft.Data.SqlClient</c> without an ORM.
/// </summary>
internal sealed class SqlServerSettingDefinitionRecordRepository(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    IJsonSerializer serializer
) : ISettingDefinitionRecordRepository
{
    /// <inheritdoc/>
    public async Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var sql =
            $"SELECT [Id],[Name],[DisplayName],[Description],[DefaultValue],[Providers],[IsVisibleToClients],[IsInherited],[IsEncrypted],[ExtraProperties] FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingDefinitionsTableName)};";

        var result = new List<SettingDefinitionRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new SettingDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)
            );

            foreach (var (key, value) in _DeserializeExtraProperties(reader.GetString(9)))
            {
                record.ExtraProperties[key] = value;
            }

            result.Add(record);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in addedRecords)
        {
            await _ExecuteAsync(connection, (SqlTransaction)transaction, _InsertSql(), record, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in changedRecords)
        {
            await _ExecuteAsync(connection, (SqlTransaction)transaction, _UpdateSql(), record, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var record in deletedRecords)
        {
            await using var command = new SqlCommand(_DeleteSql(), connection, (SqlTransaction)transaction)
            {
                CommandTimeout = _CommandTimeout(),
            };
            command.Parameters.AddWithValue("@Id", record.Id);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes <paramref name="sql"/> for a single <paramref name="record"/> within an existing <paramref name="transaction"/>.</summary>
    private async Task _ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        SettingDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@DisplayName", record.DisplayName);
        command.Parameters.AddWithValue("@Description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@DefaultValue", (object?)record.DefaultValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@Providers", (object?)record.Providers ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsVisibleToClients", record.IsVisibleToClients);
        command.Parameters.AddWithValue("@IsInherited", record.IsInherited);
        command.Parameters.AddWithValue("@IsEncrypted", record.IsEncrypted);
        command.Parameters.AddWithValue(
            "@ExtraProperties",
            serializer.SerializeToString(record.ExtraProperties) ?? "{}"
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the parameterized INSERT SQL for the setting definitions table.</summary>
    private string _InsertSql() =>
        $"INSERT INTO {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingDefinitionsTableName)} ([Id],[Name],[DisplayName],[Description],[DefaultValue],[Providers],[IsVisibleToClients],[IsInherited],[IsEncrypted],[ExtraProperties]) VALUES (@Id,@Name,@DisplayName,@Description,@DefaultValue,@Providers,@IsVisibleToClients,@IsInherited,@IsEncrypted,@ExtraProperties);";

    /// <summary>Returns the parameterized UPDATE SQL for the setting definitions table.</summary>
    private string _UpdateSql() =>
        $"UPDATE {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingDefinitionsTableName)} SET [Name]=@Name,[DisplayName]=@DisplayName,[Description]=@Description,[DefaultValue]=@DefaultValue,[Providers]=@Providers,[IsVisibleToClients]=@IsVisibleToClients,[IsInherited]=@IsInherited,[IsEncrypted]=@IsEncrypted,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;";

    /// <summary>Returns the parameterized DELETE SQL for the setting definitions table.</summary>
    private string _DeleteSql() =>
        $"DELETE FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingDefinitionsTableName)} WHERE [Id]=@Id;";

    /// <summary>Deserializes <paramref name="json"/> into an <see cref="ExtraProperties"/> collection, returning an empty collection when the result is <see langword="null"/>.</summary>
    private ExtraProperties _DeserializeExtraProperties(string json) =>
        serializer.Deserialize<ExtraProperties>(json) ?? [];

    /// <summary>Returns <see cref="SqlServerSettingsOptions.CommandTimeout"/> expressed in whole seconds for use as <c>CommandTimeout</c>.</summary>
    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;
}
