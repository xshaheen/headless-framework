// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Settings.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="ISettingValueRecordRepository"/> that stores
/// setting value records directly via <c>Microsoft.Data.SqlClient</c> without an ORM.
/// Batched operations pass the whole list as one JSON parameter read through <c>OPENJSON</c>, so no table type has
/// to exist and no 2100-parameter limit applies.
/// </summary>
internal sealed class SqlServerSettingValueRecordRepository(
    IOptions<SqlServerSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    TimeProvider timeProvider
) : ISettingValueRecordRepository
{
    /// <summary>Comma-separated column list used in SELECT queries for setting value records.</summary>
    private const string _ValueColumns = "[Id],[Name],[Value],[ProviderName],[ProviderKey],[CreatedAt],[UpdatedAt]";

    /// <inheritdoc/>
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT TOP(1) {_ValueColumns} FROM {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} WHERE [Name]=@Name AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey) ORDER BY [Id];";

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
            $"SELECT {_ValueColumns} FROM {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} WHERE {string.Join(" AND ", filters)};";

        return _ReadValuesAsync(sql, cancellationToken, [.. parameters]);
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

        // One list parameter: one cached plan whatever the count, and no 2100-parameter ceiling.
        var sql =
            $"SELECT {_ValueColumns} FROM {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} WHERE {_NamesFilter} AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _ListParameter("Names", _NameElement, names),
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
            $"SELECT {_ValueColumns} FROM {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} WHERE [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

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
        var (sql, parameters) = _InsertStatement(setting);

        return _ExecuteAsync(sql, cancellationToken, parameters);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(SettingValueRecord setting, CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = _UpdateStatement(setting);

        return _ExecuteAsync(sql, cancellationToken, parameters);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(
        IReadOnlyCollection<SettingValueRecord> settings,
        CancellationToken cancellationToken = default
    )
    {
        if (settings.Count == 0)
        {
            return Task.CompletedTask;
        }

        var (sql, parameters) = _DeleteStatement(settings);

        return _ExecuteAsync(sql, cancellationToken, parameters);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        IReadOnlyCollection<SettingValueRecord> inserted,
        IReadOnlyCollection<SettingValueRecord> updated,
        IReadOnlyCollection<SettingValueRecord> deleted,
        CancellationToken cancellationToken = default
    )
    {
        var statements = new List<(string Sql, SqlParameter[] Parameters)>(inserted.Count + updated.Count + 1);
        statements.AddRange(inserted.Select(_InsertStatement));
        statements.AddRange(updated.Select(_UpdateStatement));

        if (deleted.Count != 0)
        {
            statements.Add(_DeleteStatement(deleted));
        }

        if (statements.Count == 0)
        {
            return;
        }

        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var firstUpdate = inserted.Count;
        var afterLastUpdate = firstUpdate + updated.Count;

        for (var i = 0; i < statements.Count; i++)
        {
            var (sql, parameters) = statements[i];
            await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
            command.CommandTimeout = _CommandTimeout();
            command.Parameters.AddRange(parameters);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // An update that matched no row means another writer deleted it after the caller read it. Failing rolls
            // the batch back so the caller can re-read and retry, instead of silently dropping that value.
            if (affected == 0 && i >= firstUpdate && i < afterLastUpdate)
            {
                throw new DBConcurrencyException("A value record in the batch was deleted by another writer.");
            }
        }

        // Disposing an uncommitted transaction rolls it back, so a statement that throws above undoes every
        // earlier one in the batch.
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private (string Sql, SqlParameter[] Parameters) _InsertStatement(SettingValueRecord setting)
    {
        var sql =
            $"INSERT INTO {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} ([Id],[Name],[Value],[ProviderName],[ProviderKey],[CreatedAt]) VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey,@CreatedAt);";

        // Preserve caller-supplied CreatedAt when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it at default.
        var createdAt = setting.CreatedAt == default ? timeProvider.GetUtcNow() : setting.CreatedAt;

        return (
            sql,
            [
                _Param("Id", setting.Id),
                _Param("Name", setting.Name),
                _Param("Value", setting.Value),
                _Param("ProviderName", setting.ProviderName),
                _Param("ProviderKey", setting.ProviderKey),
                _Param("CreatedAt", createdAt),
            ]
        );
    }

    private (string Sql, SqlParameter[] Parameters) _UpdateStatement(SettingValueRecord setting)
    {
        var sql =
            $"UPDATE {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} SET [Value]=@Value,[UpdatedAt]=@UpdatedAt WHERE [Id]=@Id;";

        // Preserve caller-supplied UpdatedAt when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it null/default.
        var updatedAt =
            setting.UpdatedAt is null || setting.UpdatedAt == default(DateTimeOffset)
                ? timeProvider.GetUtcNow()
                : setting.UpdatedAt.Value;

        return (sql, [_Param("Id", setting.Id), _Param("Value", setting.Value), _Param("UpdatedAt", updatedAt)]);
    }

    private (string Sql, SqlParameter[] Parameters) _DeleteStatement(IReadOnlyCollection<SettingValueRecord> settings)
    {
        // One list parameter: one cached plan whatever the count, and no 2100-parameter ceiling.
        var sql = $"DELETE FROM {SqlServerSettingsSchema.ValuesTable(storageOptions.Value)} WHERE {_IdsFilter};";

        return (sql, [_ListParameter("Ids", SqlColumnType.Guid, settings.Select(setting => setting.Id).ToList())]);
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

    /// <summary>Returns <see cref="SqlServerSettingsOptions.CommandTimeout"/> expressed in whole seconds for use as <c>CommandTimeout</c>.</summary>
    private int _CommandTimeout()
    {
        return (int)providerOptions.Value.CommandTimeout.TotalSeconds;
    }

    /// <summary>Creates a <see cref="SqlParameter"/> prefixed with <c>@</c> named <paramref name="name"/> with <paramref name="value"/>, substituting <see cref="DBNull.Value"/> for <see langword="null"/>.</summary>
    private static SqlParameter _Param(string name, object? value)
    {
        return new($"@{name}", value ?? DBNull.Value);
    }

    private static readonly SqlColumnType _NameElement = SqlColumnType.KeyText(
        SettingValueRecordConstants.NameMaxLength
    );

    private static readonly string _NamesFilter = SqlServerDialect.Instance.InList("[Name]", "Names", _NameElement);

    private static readonly string _IdsFilter = SqlServerDialect.Instance.InList("[Id]", "Ids", SqlColumnType.Guid);

    private static SqlParameter _ListParameter<T>(string name, SqlColumnType elementType, IReadOnlyCollection<T> values)
    {
        return (SqlParameter)SqlServerDialect.Instance.CreateListParameter(name, elementType, values);
    }
}
