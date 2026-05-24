// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable CA2100 // SQL text only interpolates validated schema/table identifiers; values remain parameterized.
namespace Headless.Settings.PostgreSql;

public sealed class PostgreSqlSettingValueRecordRepository(
    IOptions<PostgreSqlSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    IServiceProvider services
) : ISettingValueRecordRepository
{
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "Name"=@Name AND "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey ORDER BY "Id" LIMIT 1;""";

        return await _ReadValuesAsync(sql, cancellationToken, _Param("Name", name), _Param("ProviderName", providerName), _Param("ProviderKey", providerKey)).ConfigureAwait(false) is [var row, ..]
            ? row
            : null;
    }

    public Task<List<SettingValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var filters = new List<string> { @"""Name""=@Name" };
        var parameters = new List<NpgsqlParameter> { _Param("Name", name) };

        if (providerName is not null)
        {
            filters.Add(@"""ProviderName""=@ProviderName");
            parameters.Add(_Param("ProviderName", providerName));
        }

        if (providerKey is not null)
        {
            filters.Add(@"""ProviderKey""=@ProviderKey");
            parameters.Add(_Param("ProviderKey", providerKey));
        }

        var sql =
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE {string.Join(" AND ", filters)};""";

        return _ReadValuesAsync(sql, cancellationToken, parameters.ToArray());
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "Name" = ANY(@Names) AND "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey;""";

        return _ReadValuesAsync(sql, cancellationToken, _Param("Names", names.ToArray()), _Param("ProviderName", providerName), _Param("ProviderKey", providerKey));
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey;""";

        return _ReadValuesAsync(sql, cancellationToken, _Param("ProviderName", providerName), _Param("ProviderKey", providerKey));
    }

    public async Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""INSERT INTO {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} ("Id","Name","Value","ProviderName","ProviderKey","DateCreated") VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey,@DateCreated);""";

        await _ExecuteAsync(
                sql,
                cancellationToken,
                _Param("Id", setting.Id),
                _Param("Name", setting.Name),
                _Param("Value", setting.Value),
                _Param("ProviderName", setting.ProviderName),
                _Param("ProviderKey", setting.ProviderKey),
                _Param("DateCreated", DateTimeOffset.UtcNow)
            )
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""UPDATE {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} SET "Value"=@Value,"DateUpdated"=@DateUpdated WHERE "Id"=@Id;""";

        await _ExecuteAsync(sql, cancellationToken, _Param("Id", setting.Id), _Param("Value", setting.Value), _Param("DateUpdated", DateTimeOffset.UtcNow)).ConfigureAwait(false);
        await _PublishAsync(setting, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        IReadOnlyCollection<SettingValueRecord> settings,
        CancellationToken cancellationToken = default
    )
    {
        if (settings.Count == 0)
        {
            return;
        }

        var sql =
            $"""DELETE FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "Id" = ANY(@Ids);""";

        await _ExecuteAsync(sql, cancellationToken, _Param("Ids", settings.Select(x => x.Id).ToArray())).ConfigureAwait(false);

        foreach (var setting in settings)
        {
            await _PublishAsync(setting, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<SettingValueRecord>> _ReadValuesAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters
    )
    {
        var result = new List<SettingValueRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                new SettingValueRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4)
                )
            );
        }

        return result;
    }

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _PublishAsync(SettingValueRecord setting, CancellationToken cancellationToken)
    {
        var publisher = services.GetService<ILocalMessagePublisher>();

        if (publisher is not null)
        {
            await publisher.PublishAsync(new EntityChangedEventData<SettingValueRecord>(setting), cancellationToken);
        }
    }

    private static NpgsqlParameter _Param(string name, object? value) => new(name, value ?? DBNull.Value);
}
