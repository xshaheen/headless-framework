// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Sql;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IFeatureValueRecordRepository"/> that reads and
/// writes feature value records using raw ADO.NET. Bulk deletes and by-name reads pass the whole list as one JSON
/// parameter read through <c>OPENJSON</c>, so no table type has to exist and no 2100-parameter ceiling applies.
/// </summary>
internal sealed class SqlServerFeatureValueRecordRepository(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions,
    TimeProvider timeProvider
) : IFeatureValueRecordRepository
{
    /// <summary>Comma-separated column list used in SELECT queries for feature value records.</summary>
    private const string _ValueColumns = "[Id],[Name],[Value],[ProviderName],[ProviderKey],[CreatedAt],[UpdatedAt]";

    /// <inheritdoc/>
    public async Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT TOP(1) {_ValueColumns} FROM {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} WHERE [Name]=@Name AND (([ProviderName] IS NULL AND @ProviderName IS NULL) OR [ProviderName]=@ProviderName) AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey) ORDER BY [Id];";

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
    public Task<List<FeatureValueRecord>> FindAllAsync(
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
            $"SELECT {_ValueColumns} FROM {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} WHERE {string.Join(" AND ", filters)};";

        return _ReadValuesAsync(sql, cancellationToken, [.. parameters]);
    }

    /// <inheritdoc/>
    public Task<List<FeatureValueRecord>> GetListAsync(
        HashSet<string> names,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (names.Count == 0)
        {
            return Task.FromResult(new List<FeatureValueRecord>());
        }

        // One list parameter: one cached plan whatever the count, and no 2100-parameter ceiling.
        var sql =
            $"SELECT {_ValueColumns} FROM {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} WHERE {_NamesFilter} AND [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _ListParameter("Names", _NameElement, names),
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey)
        );
    }

    /// <inheritdoc/>
    public Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT {_ValueColumns} FROM {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} WHERE [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey)
        );
    }

    /// <inheritdoc/>
    public Task InsertAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = _InsertStatement(feature);

        return _ExecuteAsync(sql, cancellationToken, parameters);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = _UpdateStatement(feature);

        return _ExecuteAsync(sql, cancellationToken, parameters);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> features,
        CancellationToken cancellationToken = default
    )
    {
        if (features.Count == 0)
        {
            return Task.CompletedTask;
        }

        var (sql, parameters) = _DeleteStatement(features);

        return _ExecuteAsync(sql, cancellationToken, parameters);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
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

    private (string Sql, SqlParameter[] Parameters) _InsertStatement(FeatureValueRecord feature)
    {
        var sql =
            $"INSERT INTO {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} ([Id],[Name],[Value],[ProviderName],[ProviderKey],[CreatedAt]) VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey,@CreatedAt);";

        // Preserve caller-supplied CreatedAt when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it at default.
        var createdAt = feature.CreatedAt == default ? timeProvider.GetUtcNow() : feature.CreatedAt;

        return (
            sql,
            [
                _Param("Id", feature.Id),
                _Param("Name", feature.Name),
                _Param("Value", feature.Value),
                _Param("ProviderName", feature.ProviderName),
                _Param("ProviderKey", feature.ProviderKey),
                _Param("CreatedAt", createdAt),
            ]
        );
    }

    private (string Sql, SqlParameter[] Parameters) _UpdateStatement(FeatureValueRecord feature)
    {
        var sql =
            $"UPDATE {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} SET [Value]=@Value,[UpdatedAt]=@UpdatedAt WHERE [Id]=@Id;";

        // Preserve caller-supplied UpdatedAt when present (mirrors the EF path); only stamp from
        // the TimeProvider when the caller left it null/default.
        var updatedAt =
            feature.UpdatedAt is null || feature.UpdatedAt == default(DateTimeOffset)
                ? timeProvider.GetUtcNow()
                : feature.UpdatedAt.Value;

        return (sql, [_Param("Id", feature.Id), _Param("Value", feature.Value), _Param("UpdatedAt", updatedAt)]);
    }

    private (string Sql, SqlParameter[] Parameters) _DeleteStatement(IReadOnlyCollection<FeatureValueRecord> features)
    {
        // One list parameter: one cached plan whatever the count, and no 2100-parameter ceiling.
        var sql = $"DELETE FROM {SqlServerFeaturesSchema.ValuesTable(storageOptions.Value)} WHERE {_IdsFilter};";

        return (sql, [_ListParameter("Ids", SqlColumnType.Guid, features.Select(feature => feature.Id).ToList())]);
    }

    private async Task<List<FeatureValueRecord>> _ReadValuesAsync(
        string sql,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters
    )
    {
        var result = new List<FeatureValueRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(
                FeatureValueRecord.FromStorage(
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

    private int _CommandTimeout()
    {
        return (int)providerOptions.Value.CommandTimeout.TotalSeconds;
    }

    private static SqlParameter _Param(string name, object? value)
    {
        return new($"@{name}", value ?? DBNull.Value);
    }

    private static readonly SqlColumnType _NameElement = SqlColumnType.KeyText(
        FeatureValueRecordConstants.NameMaxLength
    );

    private static readonly string _NamesFilter = SqlServerDialect.Instance.InList("[Name]", "Names", _NameElement);

    private static readonly string _IdsFilter = SqlServerDialect.Instance.InList("[Id]", "Ids", SqlColumnType.Guid);

    private static SqlParameter _ListParameter<T>(string name, SqlColumnType elementType, IReadOnlyCollection<T> values)
    {
        return (SqlParameter)SqlServerDialect.Instance.CreateListParameter(name, elementType, values);
    }
}
