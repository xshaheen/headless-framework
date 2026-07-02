// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable CA2100 // SQL text only interpolates validated schema/table identifiers; values remain parameterized.
namespace Headless.Features.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IFeatureValueRecordRepository"/> that reads and
/// writes feature value records using raw ADO.NET.
/// </summary>
internal sealed class PostgreSqlFeatureValueRecordRepository(
    IOptions<PostgreSqlFeaturesOptions> providerOptions,
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
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE "Name"=@Name AND "ProviderName" IS NOT DISTINCT FROM @ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey ORDER BY "Id" LIMIT 1;""";

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
        var filters = new List<string>
        {
            """
                "Name"=@Name
                """,
        };
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
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE {string.Join(" AND ", filters)};""";

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
            $"""SELECT "Id","Name","Value","ProviderName","ProviderKey" FROM {PostgreSqlFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE "ProviderName"=@ProviderName AND "ProviderKey" IS NOT DISTINCT FROM @ProviderKey;""";

        return _ReadValuesAsync(
            sql,
            cancellationToken,
            _Param("ProviderName", providerName),
            _Param("ProviderKey", providerKey)
        );
    }

    /// <inheritdoc/>
    public async Task InsertAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""INSERT INTO {PostgreSqlFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} ("Id","Name","Value","ProviderName","ProviderKey") VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey);""";

        await _ExecuteAsync(
                sql,
                cancellationToken,
                _Param("Id", feature.Id),
                _Param("Name", feature.Name),
                _Param("Value", feature.Value),
                _Param("ProviderName", feature.ProviderName),
                _Param("ProviderKey", feature.ProviderKey)
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""UPDATE {PostgreSqlFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} SET "Value"=@Value WHERE "Id"=@Id;""";

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

        var sql =
            $"""DELETE FROM {PostgreSqlFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE "Id" = ANY(@Ids);""";

        await _ExecuteAsync(sql, cancellationToken, _Param("Ids", features.Select(x => x.Id).ToArray()))
            .ConfigureAwait(false);
    }

    private async Task<List<FeatureValueRecord>> _ReadValuesAsync(
        string sql,
        CancellationToken cancellationToken,
        params NpgsqlParameter[] parameters
    )
    {
        var result = new List<FeatureValueRecord>();
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
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

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    private static NpgsqlParameter _Param(string name, object? value) => new(name, value ?? DBNull.Value);
}
