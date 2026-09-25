// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Settings.Entities;
using Headless.Settings.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable CA2100 // SQL text only interpolates validated schema/table identifiers; values remain parameterized.
namespace Headless.Settings.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="ISettingValueRecordRepository"/> that stores
/// setting value records directly via Npgsql without an ORM.
/// </summary>
internal sealed class PostgreSqlSettingValueRecordRepository(
    IOptions<PostgreSqlSettingsOptions> providerOptions,
    IOptions<SettingsStorageOptions> storageOptions,
    TimeProvider timeProvider
) : ISettingValueRecordRepository
{
    /// <summary>Comma-separated column list used in SELECT queries for setting value records.</summary>
    private const string _ValueColumns =
        @"""Id"",""Name"",""Value"",""ProviderName"",""ProviderKey"",""CreatedAt"",""UpdatedAt""";

    /// <inheritdoc/>
    public async Task<SettingValueRecord?> FindAsync(
        string name,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT {_ValueColumns} FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "Name"=@Name AND "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey ORDER BY "Id" LIMIT 1;""";

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
            $"SELECT {_ValueColumns} FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE {string.Join(" AND ", filters)};";

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
        var sql =
            $"""SELECT {_ValueColumns} FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "Name" = ANY(@Names) AND "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey;""";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _Param("Names", names.ToArray()),
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
            $"""SELECT {_ValueColumns} FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey;""";

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
        var statements = new List<(string Sql, NpgsqlParameter[] Parameters)>(inserted.Count + updated.Count + 1);
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
            await using var command = new NpgsqlCommand(sql, connection, transaction);
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

    private (string Sql, NpgsqlParameter[] Parameters) _InsertStatement(SettingValueRecord setting)
    {
        var sql =
            $"""INSERT INTO {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} ("Id","Name","Value","ProviderName","ProviderKey","CreatedAt") VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey,@CreatedAt);""";

        // Preserve caller-supplied CreatedAt when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it at default. Tests that pin CreatedAt for
        // deterministic assertions and audit-driven scenarios that backfill historical timestamps
        // both depend on this round-trip.
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

    private (string Sql, NpgsqlParameter[] Parameters) _UpdateStatement(SettingValueRecord setting)
    {
        var sql =
            $"""UPDATE {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} SET "Value"=@Value,"UpdatedAt"=@UpdatedAt WHERE "Id"=@Id;""";

        // Preserve caller-supplied UpdatedAt when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it null/default.
        var updatedAt =
            setting.UpdatedAt is null || setting.UpdatedAt == default(DateTimeOffset)
                ? timeProvider.GetUtcNow()
                : setting.UpdatedAt.Value;

        return (sql, [_Param("Id", setting.Id), _Param("Value", setting.Value), _Param("UpdatedAt", updatedAt)]);
    }

    private (string Sql, NpgsqlParameter[] Parameters) _DeleteStatement(
        IReadOnlyCollection<SettingValueRecord> settings
    )
    {
        var sql =
            $"""DELETE FROM {PostgreSqlSettingsStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.SettingValuesTableName)} WHERE "Id" = ANY(@Ids);""";

        return (sql, [_Param("Ids", settings.Select(x => x.Id).ToArray())]);
    }

    /// <summary>Opens a new connection, executes <paramref name="sql"/> with <paramref name="parameters"/>, and maps each row to a <see cref="SettingValueRecord"/>.</summary>
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
    private async Task _ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters
    )
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns <see cref="PostgreSqlSettingsOptions.CommandTimeout"/> expressed in whole seconds for use as <c>CommandTimeout</c>.</summary>
    private int _CommandTimeout()
    {
        return (int)providerOptions.Value.CommandTimeout.TotalSeconds;
    }

    /// <summary>Creates an <see cref="NpgsqlParameter"/> named <paramref name="name"/> with <paramref name="value"/>, substituting <see cref="DBNull.Value"/> for <see langword="null"/>.</summary>
    private static NpgsqlParameter _Param(string name, object? value)
    {
        return new(name, value ?? DBNull.Value);
    }
}
