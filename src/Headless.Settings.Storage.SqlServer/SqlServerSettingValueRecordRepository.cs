// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Domain;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

internal sealed class SqlServerSettingValueRecordRepository(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    IServiceProvider services
) : ISettingValueRecordRepository
{
    private const string _ValueColumns = "[Id],[Name],[Value],[ProviderName],[ProviderKey],[DateCreated],[DateUpdated]";

    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT TOP(1) {_ValueColumns} FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [Name]=@Name AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey) ORDER BY [Id];";

        return
            await _ReadValuesAsync(
                    sql,
                    cancellationToken,
                    _Param("Name", name),
                    _Param("ProviderName", providerName),
                    _Param("ProviderKey", providerKey)
                )
                .ConfigureAwait(false)
                is [var row, ..]
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
            $"SELECT {_ValueColumns} FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE {string.Join(" AND ", filters)};";

        return _ReadValuesAsync(sql, cancellationToken, parameters.ToArray());
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Count == 0)
        {
            return Task.FromResult(new List<SettingValueRecord>());
        }

        // Pass the names through the HeadlessSettingsNameList TVP: one cached plan regardless of count and
        // no 2100-parameter ceiling, portable to older engines (no OPENJSON / compatibility level 130).
        var sql =
            $"SELECT {_ValueColumns} FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [Name] IN (SELECT [Name] FROM @Names) AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _BuildNameListTvpParameter(names),
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey)
        );
    }

    public Task<List<SettingValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT {_ValueColumns} FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey)
        );
    }

    public Task InsertAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var sql =
            $"INSERT INTO {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} ([Id],[Name],[Value],[ProviderName],[ProviderKey],[DateCreated]) VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey,@DateCreated);";

        // Preserve caller-supplied DateCreated when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it at default.
        var dateCreated = setting.DateCreated == default ? _TimeProvider().GetUtcNow() : setting.DateCreated;

        return _ExecuteAsync(
            sql,
            cancellationToken,
            _Param("Id", setting.Id),
            _Param("Name", setting.Name),
            _Param("Value", setting.Value),
            _Param("ProviderName", setting.ProviderName),
            _Param("ProviderKey", setting.ProviderKey),
            _Param("DateCreated", dateCreated)
        );
    }

    public async Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var sql =
            $"UPDATE {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} SET [Value]=@Value,[DateUpdated]=@DateUpdated WHERE [Id]=@Id;";

        // Preserve caller-supplied DateUpdated when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it null/default.
        var dateUpdated =
            setting.DateUpdated is null || setting.DateUpdated == default(DateTimeOffset)
                ? _TimeProvider().GetUtcNow()
                : setting.DateUpdated.Value;

        await _ExecuteAsync(
                sql,
                cancellationToken,
                _Param("Id", setting.Id),
                _Param("Value", setting.Value),
                _Param("DateUpdated", dateUpdated)
            )
            .ConfigureAwait(false);
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

        // Pass ids through the HeadlessSettingsIdList TVP: one cached plan regardless of count, no
        // 2100-parameter ceiling, portable to older engines (no OPENJSON / compatibility level 130).
        var sql =
            $"DELETE FROM {SqlServerSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE [Id] IN (SELECT [Id] FROM @Ids);";

        await _ExecuteAsync(sql, cancellationToken, _BuildIdListTvpParameter(settings.Select(setting => setting.Id)))
            .ConfigureAwait(false);

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
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);

        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                SettingValueRecord.FromStorage(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                    await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken).ConfigureAwait(false),
                    await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false)
                        ? null
                        : await reader.GetFieldValueAsync<DateTimeOffset>(6, cancellationToken).ConfigureAwait(false)
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

    private async ValueTask _PublishAsync(SettingValueRecord setting, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetService<ILocalEventBus>();

        if (publisher is not null)
        {
            await publisher
                .PublishAsync(new EntityChangedEventData<SettingValueRecord>(setting), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    private TimeProvider _TimeProvider() => services.GetService<TimeProvider>() ?? TimeProvider.System;

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
            TypeName = $"[{storageOptions.Value.Schema}].[HeadlessSettingsIdList]",
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
            TypeName = $"[{storageOptions.Value.Schema}].[HeadlessSettingsNameList]",
            Value = namesTable,
        };
    }

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
