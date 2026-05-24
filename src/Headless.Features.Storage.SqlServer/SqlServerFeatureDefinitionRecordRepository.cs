// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Primitives;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

public sealed class SqlServerFeatureDefinitionRecordRepository(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions
) : IFeatureDefinitionRecordRepository
{
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT [Id],[GroupName],[Name],[ParentName],[DisplayName],[Description],[DefaultValue],[IsVisibleToClients],[IsAvailableToHost],[Providers],[ExtraProperties] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)};""";

        var result = new List<FeatureDefinitionRecord>();
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
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

    public async Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT [Id],[Name],[DisplayName],[ExtraProperties] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)};""";

        var result = new List<FeatureGroupDefinitionRecord>();
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
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
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in newGroups)
        {
            await _ExecuteGroupAsync(connection, (SqlTransaction)transaction, _InsertGroupSql(), record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in updatedGroups)
        {
            await _ExecuteGroupAsync(connection, (SqlTransaction)transaction, _UpdateGroupSql(), record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in deletedGroups)
        {
            await _DeleteAsync(connection, (SqlTransaction)transaction, _DeleteGroupSql(), record.Id, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in newFeatures)
        {
            await _ExecuteFeatureAsync(connection, (SqlTransaction)transaction, _InsertFeatureSql(), record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in updatedFeatures)
        {
            await _ExecuteFeatureAsync(connection, (SqlTransaction)transaction, _UpdateFeatureSql(), record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in deletedFeatures)
        {
            await _DeleteAsync(connection, (SqlTransaction)transaction, _DeleteFeatureSql(), record.Id, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task _ExecuteGroupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        FeatureGroupDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@Name", record.Name);
        command.Parameters.AddWithValue("@DisplayName", record.DisplayName);
        command.Parameters.AddWithValue("@ExtraProperties", JsonSerializer.Serialize(record.ExtraProperties, _JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task _ExecuteFeatureAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        FeatureDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
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
        command.Parameters.AddWithValue("@ExtraProperties", JsonSerializer.Serialize(record.ExtraProperties, _JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task _DeleteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string _InsertGroupSql() =>
        $"""INSERT INTO {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)} ([Id],[Name],[DisplayName],[ExtraProperties]) VALUES (@Id,@Name,@DisplayName,@ExtraProperties);""";

    private string _UpdateGroupSql() =>
        $"""UPDATE {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)} SET [Name]=@Name,[DisplayName]=@DisplayName,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;""";

    private string _DeleteGroupSql() =>
        $"""DELETE FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureGroupDefinitionsTableName)} WHERE [Id]=@Id;""";

    private string _InsertFeatureSql() =>
        $"""INSERT INTO {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)} ([Id],[GroupName],[Name],[DisplayName],[ParentName],[Description],[DefaultValue],[IsVisibleToClients],[IsAvailableToHost],[Providers],[ExtraProperties]) VALUES (@Id,@GroupName,@Name,@DisplayName,@ParentName,@Description,@DefaultValue,@IsVisibleToClients,@IsAvailableToHost,@Providers,@ExtraProperties);""";

    private string _UpdateFeatureSql() =>
        $"""UPDATE {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)} SET [GroupName]=@GroupName,[Name]=@Name,[DisplayName]=@DisplayName,[ParentName]=@ParentName,[Description]=@Description,[DefaultValue]=@DefaultValue,[IsVisibleToClients]=@IsVisibleToClients,[IsAvailableToHost]=@IsAvailableToHost,[Providers]=@Providers,[ExtraProperties]=@ExtraProperties WHERE [Id]=@Id;""";

    private string _DeleteFeatureSql() =>
        $"""DELETE FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureDefinitionsTableName)} WHERE [Id]=@Id;""";

    private static ExtraProperties _DeserializeExtraProperties(string json) =>
        JsonSerializer.Deserialize<ExtraProperties>(json, _JsonOptions) ?? [];
}
