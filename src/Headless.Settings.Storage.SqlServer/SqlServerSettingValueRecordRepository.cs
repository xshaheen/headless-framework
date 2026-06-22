// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Domain;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="ISettingValueRecordRepository"/> that stores
/// setting value records directly via <c>Microsoft.Data.SqlClient</c> without an ORM.
/// Uses table-valued parameters (TVPs) for batched operations to avoid the 2100-parameter limit.
/// </summary>
internal sealed class SqlServerSettingValueRecordRepository(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    IServiceProvider services
) : ISettingValueRecordRepository
{
    /// <summary>Comma-separated column list used in SELECT queries for setting value records.</summary>
    private const string _ValueColumns = "[Id],[Name],[Value],[ProviderName],[ProviderKey],[DateCreated],[DateUpdated]";

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>Opens a new connection, executes <paramref name="sql"/> with <paramref name="parameters"/>, and maps each row to a <see cref="SettingValueRecord"/>.</summary>
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

    /// <summary>Opens a new connection and executes a non-query <paramref name="sql"/> statement with <paramref name="parameters"/>.</summary>
    private async Task _ExecuteAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Publishes an <see cref="Headless.Domain.EntityChangedEventData{T}"/> event for <paramref name="setting"/> when an <see cref="Headless.Domain.ILocalEventBus"/> is registered in the container.</summary>
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

    /// <summary>Returns <see cref="SqlServerSettingsOptions.CommandTimeout"/> expressed in whole seconds for use as <c>CommandTimeout</c>.</summary>
    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    /// <summary>Resolves the registered <see cref="TimeProvider"/>, falling back to <see cref="TimeProvider.System"/> when not registered.</summary>
    private TimeProvider _TimeProvider() => services.GetService<TimeProvider>() ?? TimeProvider.System;

    /// <summary>Builds a structured <c>@Ids</c> TVP parameter containing the supplied <paramref name="ids"/>, typed as <c>HeadlessSettingsIdList</c>.</summary>
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

    /// <summary>Builds a structured <c>@Names</c> TVP parameter containing the supplied <paramref name="names"/>, typed as <c>HeadlessSettingsNameList</c>.</summary>
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

    /// <summary>Creates a <see cref="SqlParameter"/> prefixed with <c>@</c> named <paramref name="name"/> with <paramref name="value"/>, substituting <see cref="DBNull.Value"/> for <see langword="null"/>.</summary>
    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
