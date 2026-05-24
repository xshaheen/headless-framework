// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

public sealed class SqlServerSettingValueRecordRepository(
    IOptions<SqlServerSettingsOptions> providerOptions,
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
            $"""SELECT TOP(1) [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [Name]=@Name AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey) ORDER BY [Id];""";

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
        var filters = new List<string> { "[Name]=@Name" };
        var parameters = new List<SqlParameter> { _Param("Name", name) };

        if (providerName is not null)
        {
            filters.Add("[ProviderName]=@ProviderName");
            parameters.Add(_Param("ProviderName", providerName));
        }

        if (providerKey is not null)
        {
            filters.Add("[ProviderKey]=@ProviderKey");
            parameters.Add(_Param("ProviderKey", providerKey));
        }

        var sql =
            $"""SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE {string.Join(" AND ", filters)};""";

        return _ReadValuesAsync(sql, cancellationToken, parameters.ToArray());
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var nameParameters = names.Select((_, index) => $"@Name{index}").ToArray();
        var parameters = names.Select((name, index) => _Param($"Name{index}", name)).ToList();
        parameters.Add(_Param("ProviderName", providerName));
        parameters.Add(_Param("ProviderKey", providerKey));

        var sql =
            $"""SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [Name] IN ({string.Join(",", nameParameters)}) AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);""";

        return _ReadValuesAsync(sql, cancellationToken, parameters.ToArray());
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);""";

        return _ReadValuesAsync(sql, cancellationToken, _Param("ProviderName", providerName), _Param("ProviderKey", providerKey));
    }

    public Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""INSERT INTO {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} ([Id],[Name],[Value],[ProviderName],[ProviderKey],[DateCreated]) VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey,@DateCreated);""";

        return _ExecuteAsync(
            sql,
            cancellationToken,
            _Param("Id", setting.Id),
            _Param("Name", setting.Name),
            _Param("Value", setting.Value),
            _Param("ProviderName", setting.ProviderName),
            _Param("ProviderKey", setting.ProviderKey),
            _Param("DateCreated", DateTimeOffset.UtcNow)
        );
    }

    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""UPDATE {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} SET [Value]=@Value,[DateUpdated]=@DateUpdated WHERE [Id]=@Id;""";

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

        var idParameters = settings.Select((_, index) => $"@Id{index}").ToArray();
        var parameters = settings.Select((setting, index) => _Param($"Id{index}", setting.Id)).ToArray();
        var sql =
            $"""DELETE FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [Id] IN ({string.Join(",", idParameters)});""";

        await _ExecuteAsync(sql, cancellationToken, parameters).ConfigureAwait(false);

        foreach (var setting in settings)
        {
            await _PublishAsync(setting, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<SettingValueRecord>> _ReadValuesAsync(
        string sql,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters
    )
    {
        var result = new List<SettingValueRecord>();
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
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

    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
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

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
