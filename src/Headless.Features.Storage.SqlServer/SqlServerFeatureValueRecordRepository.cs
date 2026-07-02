// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IFeatureValueRecordRepository"/> that reads and
/// writes feature value records using raw ADO.NET. Bulk deletes use a table-valued parameter
/// (<c>HeadlessFeaturesIdList</c>) to avoid the 2100-parameter ceiling.
/// </summary>
internal sealed class SqlServerFeatureValueRecordRepository(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions
) : IFeatureValueRecordRepository
{
    /// <inheritdoc/>
    public async Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT TOP(1) [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE [Name]=@Name AND (([ProviderName] IS NULL AND @ProviderName IS NULL) OR [ProviderName]=@ProviderName) AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey) ORDER BY [Id];";

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
            $"SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE {string.Join(" AND ", filters)};";

        return _ReadValuesAsync(sql, cancellationToken, [.. parameters]);
    }

    /// <inheritdoc/>
    public Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);";

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
        var sql =
            $"INSERT INTO {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} ([Id],[Name],[Value],[ProviderName],[ProviderKey]) VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey);";

        return _ExecuteAsync(
            sql,
            cancellationToken,
            _Param("Id", feature.Id),
            _Param("Name", feature.Name),
            _Param("Value", feature.Value),
            _Param("ProviderName", feature.ProviderName),
            _Param("ProviderKey", feature.ProviderKey)
        );
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var sql =
            $"UPDATE {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} SET [Value]=@Value WHERE [Id]=@Id;";

        await _ExecuteAsync(sql, cancellationToken, _Param("Id", feature.Id), _Param("Value", feature.Value))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> features,
        CancellationToken cancellationToken = default
    )
    {
        if (features.Count == 0)
        {
            return;
        }

        // Pass ids through the HeadlessFeaturesIdList TVP: one cached plan regardless of count, no
        // 2100-parameter ceiling, portable to older engines (no OPENJSON / compatibility level 130).
        var sql =
            $"DELETE FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE [Id] IN (SELECT [Id] FROM @Ids);";

        await _ExecuteAsync(sql, cancellationToken, _BuildIdListTvpParameter(features.Select(feature => feature.Id)))
            .ConfigureAwait(false);
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
                new FeatureValueRecord(
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
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.CommandTimeout = _CommandTimeout();
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

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
            TypeName = $"[{storageOptions.Value.Schema}].[HeadlessFeaturesIdList]",
            Value = idsTable,
        };
    }

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
