// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Primitives;
using Headless.Serializer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IFeatureDefinitionRecordRepository"/> that reads and
/// writes feature-group and feature-definition records using raw ADO.NET and batched
/// multi-row <c>INSERT</c> statements for efficiency.
/// </summary>
internal sealed class SqlServerFeatureDefinitionRecordRepository(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions,
    IJsonSerializer serializer
) : IFeatureDefinitionRecordRepository
{
    /// <summary>
    /// Maximum number of rows sent in a single batched INSERT statement.
    /// SQL Server limits parameters to 2100 per statement; 100 rows × 11 columns = 1100 parameters.
    /// </summary>
    private const int _MaxRowsPerInsert = 100;

    /// <summary>Cache of parameterized INSERT SQL strings keyed by row count (groups).</summary>
    private readonly ConcurrentDictionary<int, string> _insertGroupBatchSql = new();

    /// <summary>Cache of parameterized INSERT SQL strings keyed by row count (features).</summary>
    private readonly ConcurrentDictionary<int, string> _insertFeatureBatchSql = new();

    private string? _updateGroupSql;
    private string? _deleteGroupSql;
    private string? _updateFeatureSql;
    private string? _deleteFeatureSql;
    private string? _selectFeaturesSql;
    private string? _selectGroupsSql;

    /// <inheritdoc/>
    public async Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default)
    {
        var sql = _selectFeaturesSql ??=
            $"SELECT [Id],[GroupName],[Name],[ParentName],[DisplayName],[Description],[DefaultValue],[IsVisibleToClients],[IsAvailableToHost],[Providers],[ExtraProperties] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)};";

        var result = new List<FeatureDefinitionRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);

        command.CommandTimeout = _CommandTimeout();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new FeatureDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
                await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9)
            );

            foreach (var (key, value) in _DeserializeExtraProperties(reader.GetString(10)))
            {
                record.ExtraProperties[key] = value;
            }

            result.Add(record);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sql = _selectGroupsSql ??=
            $"SELECT [Id],[Name],[DisplayName],[ExtraProperties] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)};";

        var result = new List<FeatureGroupDefinitionRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new FeatureGroupDefinitionRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));

            foreach (var (key, value) in _DeserializeExtraProperties(reader.GetString(3)))
            {
                record.ExtraProperties[key] = value;
            }

            result.Add(record);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        List<FeatureGroupDefinitionRecord> newGroups,
        List<FeatureGroupDefinitionRecord> updatedGroups,
        List<FeatureGroupDefinitionRecord> deletedGroups,
        List<FeatureDefinitionRecord> newFeatures,
        List<FeatureDefinitionRecord> updatedFeatures,
        List<FeatureDefinitionRecord> deletedFeatures,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)
            await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _BatchInsertGroupsAsync(connection, transaction, newGroups, cancellationToken).ConfigureAwait(false);

        foreach (var record in updatedGroups)
        {
            await _ExecuteGroupAsync(
                    connection,
                    transaction,
                    _UpdateGroupSql(),
                    _CommandTimeout(),
                    record,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var record in deletedGroups)
        {
            await _DeleteAsync(
                    connection,
                    transaction,
                    _DeleteGroupSql(),
                    _CommandTimeout(),
                    record.Id,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await _BatchInsertFeaturesAsync(connection, transaction, newFeatures, cancellationToken).ConfigureAwait(false);

        foreach (var record in updatedFeatures)
        {
            await _ExecuteFeatureAsync(
                    connection,
                    transaction,
                    _UpdateFeatureSql(),
                    _CommandTimeout(),
                    record,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        foreach (var record in deletedFeatures)
        {
            await _DeleteAsync(
                    connection,
                    transaction,
                    _DeleteFeatureSql(),
                    _CommandTimeout(),
                    record.Id,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _BatchInsertGroupsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        List<FeatureGroupDefinitionRecord> records,
        CancellationToken cancellationToken
    )
    {
        for (var offset = 0; offset < records.Count; offset += _MaxRowsPerInsert)
        {
            var rowCount = Math.Min(_MaxRowsPerInsert, records.Count - offset);
            var sql = _insertGroupBatchSql.GetOrAdd(rowCount, _BuildInsertGroupSql);

            await using var command = new SqlCommand(sql, connection, transaction);
            command.CommandTimeout = _CommandTimeout();

            for (var i = 0; i < rowCount; i++)
            {
                var record = records[offset + i];
                command.Parameters.AddWithValue(string.Create(CultureInfo.InvariantCulture, $"@Id_{i}"), record.Id);
                command.Parameters.AddWithValue(string.Create(CultureInfo.InvariantCulture, $"@Name_{i}"), record.Name);
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@DisplayName_{i}"),
                    record.DisplayName
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@ExtraProperties_{i}"),
                    serializer.SerializeToString(record.ExtraProperties) ?? "{}"
                );
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _BatchInsertFeaturesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        List<FeatureDefinitionRecord> records,
        CancellationToken cancellationToken
    )
    {
        for (var offset = 0; offset < records.Count; offset += _MaxRowsPerInsert)
        {
            var rowCount = Math.Min(_MaxRowsPerInsert, records.Count - offset);
            var sql = _insertFeatureBatchSql.GetOrAdd(rowCount, _BuildInsertFeatureSql);

            await using var command = new SqlCommand(sql, connection, transaction);
            command.CommandTimeout = _CommandTimeout();

            for (var i = 0; i < rowCount; i++)
            {
                var record = records[offset + i];
                command.Parameters.AddWithValue(string.Create(CultureInfo.InvariantCulture, $"@Id_{i}"), record.Id);
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@GroupName_{i}"),
                    record.GroupName
                );
                command.Parameters.AddWithValue(string.Create(CultureInfo.InvariantCulture, $"@Name_{i}"), record.Name);
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@DisplayName_{i}"),
                    record.DisplayName
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@ParentName_{i}"),
                    (object?)record.ParentName ?? DBNull.Value
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@Description_{i}"),
                    (object?)record.Description ?? DBNull.Value
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@DefaultValue_{i}"),
                    (object?)record.DefaultValue ?? DBNull.Value
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@IsVisibleToClients_{i}"),
                    record.IsVisibleToClients
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@IsAvailableToHost_{i}"),
                    record.IsAvailableToHost
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@Providers_{i}"),
                    (object?)record.Providers ?? DBNull.Value
                );
                command.Parameters.AddWithValue(
                    string.Create(CultureInfo.InvariantCulture, $"@ExtraProperties_{i}"),
                    serializer.SerializeToString(record.ExtraProperties) ?? "{}"
                );
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _ExecuteGroupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        int commandTimeout,
        FeatureGroupDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = commandTimeout;
        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@DisplayName", record.DisplayName);
        command.Parameters.AddWithValue(
            "@ExtraProperties",
            serializer.SerializeToString(record.ExtraProperties) ?? "{}"
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _ExecuteFeatureAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        int commandTimeout,
        FeatureDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = commandTimeout;
        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@GroupName", record.GroupName);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@DisplayName", record.DisplayName);
        command.Parameters.AddWithValue("@ParentName", (object?)record.ParentName ?? DBNull.Value);
        command.Parameters.AddWithValue("@Description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@DefaultValue", (object?)record.DefaultValue ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsVisibleToClients", record.IsVisibleToClients);
        command.Parameters.AddWithValue("@IsAvailableToHost", record.IsAvailableToHost);
        command.Parameters.AddWithValue("@Providers", (object?)record.Providers ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ExtraProperties",
            serializer.SerializeToString(record.ExtraProperties) ?? "{}"
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task _DeleteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        int commandTimeout,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.CommandTimeout = commandTimeout;
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string _BuildInsertGroupSql(int rowCount)
    {
        var table = SqlServerFeaturesStorageInitializer.Qualified(
            storageOptions.Value,
            storageOptions.Value.FeatureGroupDefinitionsTableName
        );
        var builder = new StringBuilder(128 + (rowCount * 80));
        builder.Append("INSERT INTO ").Append(table).Append(" ([Id],[Name],[DisplayName],[ExtraProperties]) VALUES ");

        for (var i = 0; i < rowCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append("(@Id_")
                .Append(i)
                .Append(",@Name_")
                .Append(i)
                .Append(",@DisplayName_")
                .Append(i)
                .Append(",@ExtraProperties_")
                .Append(i)
                .Append(')');
        }

        builder.Append(';');
        return builder.ToString();
    }

    private string _BuildInsertFeatureSql(int rowCount)
    {
        var table = SqlServerFeaturesStorageInitializer.Qualified(
            storageOptions.Value,
            storageOptions.Value.FeatureDefinitionsTableName
        );
        var builder = new StringBuilder(192 + (rowCount * 200));
        builder.Append("INSERT INTO ").Append(table);
        builder.Append(
            " ([Id],[GroupName],[Name],[DisplayName],[ParentName],[Description],[DefaultValue],[IsVisibleToClients],[IsAvailableToHost],[Providers],[ExtraProperties]) VALUES "
        );

        for (var i = 0; i < rowCount; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append("(@Id_")
                .Append(i)
                .Append(",@GroupName_")
                .Append(i)
                .Append(",@Name_")
                .Append(i)
                .Append(",@DisplayName_")
                .Append(i)
                .Append(",@ParentName_")
                .Append(i)
                .Append(",@Description_")
                .Append(i)
                .Append(",@DefaultValue_")
                .Append(i)
                .Append(",@IsVisibleToClients_")
                .Append(i)
                .Append(",@IsAvailableToHost_")
                .Append(i)
                .Append(",@Providers_")
                .Append(i)
                .Append(",@ExtraProperties_")
                .Append(i)
                .Append(')');
        }

        builder.Append(';');
        return builder.ToString();
    }

    private string _UpdateGroupSql() =>
        _updateGroupSql ??=
            $"UPDATE {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)} SET [Name]=@Name,[DisplayName]=@DisplayName,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;";

    private string _DeleteGroupSql() =>
        _deleteGroupSql ??=
            $"DELETE FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)} WHERE [Id]=@Id;";

    private string _UpdateFeatureSql() =>
        _updateFeatureSql ??=
            $"UPDATE {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)} SET [GroupName]=@GroupName,[Name]=@Name,[DisplayName]=@DisplayName,[ParentName]=@ParentName,[Description]=@Description,[DefaultValue]=@DefaultValue,[IsVisibleToClients]=@IsVisibleToClients,[IsAvailableToHost]=@IsAvailableToHost,[Providers]=@Providers,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;";

    private string _DeleteFeatureSql() =>
        _deleteFeatureSql ??=
            $"DELETE FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)} WHERE [Id]=@Id;";

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    private ExtraProperties _DeserializeExtraProperties(string json) =>
        serializer.Deserialize<ExtraProperties>(json) ?? [];
}
