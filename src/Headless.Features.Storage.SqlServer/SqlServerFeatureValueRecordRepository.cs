// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Features.SqlServer;

internal sealed class SqlServerFeatureValueRecordRepository(
    IOptions<SqlServerFeaturesOptions> providerOptions,
    IOptions<FeaturesStorageOptions> storageOptions,
    IServiceProvider services
) : IFeatureValueRecordRepository
{
    private const int _MaxDeleteParameters = 2000;

    public async Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT TOP(1) [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE [Name]=@Name AND (([ProviderName] IS NULL AND @ProviderName IS NULL) OR [ProviderName]=@ProviderName) AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey) ORDER BY [Id];""";

        return await _ReadValuesAsync(sql, cancellationToken, _Param("Name", name), _Param("ProviderName", providerName), _Param("ProviderKey", providerKey)).ConfigureAwait(false) is [var row, ..]
            ? row
            : null;
    }

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
            $"""SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE {string.Join(" AND ", filters)};""";

        return _ReadValuesAsync(sql, cancellationToken, parameters.ToArray());
    }

    public Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var sql =
            $"""SELECT [Id],[Name],[Value],[ProviderName],[ProviderKey] FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE [ProviderName]=@ProviderName AND (([ProviderKey] IS NULL AND @ProviderKey IS NULL) OR [ProviderKey]=@ProviderKey);""";

        return _ReadValuesAsync(sql, cancellationToken, _Param("ProviderName", providerName), _Param("ProviderKey", providerKey));
    }

    public Task InsertAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""INSERT INTO {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} ([Id],[Name],[Value],[ProviderName],[ProviderKey]) VALUES (@Id,@Name,@Value,@ProviderName,@ProviderKey);""";

        return _InsertAsync(
            sql,
            cancellationToken,
            _Param("Id", feature.Id),
            _Param("Name", feature.Name),
            _Param("Value", feature.Value),
            _Param("ProviderName", feature.ProviderName),
            _Param("ProviderKey", feature.ProviderKey)
        );

        async Task _InsertAsync(string insertSql, CancellationToken ct, params SqlParameter[] parameters)
        {
            await _ExecuteAsync(insertSql, ct, parameters).ConfigureAwait(false);
            await _PublishAsync(feature, ct).ConfigureAwait(false);
        }
    }

    public async Task UpdateAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        var sql =
            $"""UPDATE {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} SET [Value]=@Value WHERE [Id]=@Id;""";

        await _ExecuteAsync(sql, cancellationToken, _Param("Id", feature.Id), _Param("Value", feature.Value)).ConfigureAwait(false);
        await _PublishAsync(feature, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> features,
        CancellationToken cancellationToken = default
    )
    {
        if (features.Count == 0)
        {
            return;
        }

        foreach (var chunk in features.Chunk(_MaxDeleteParameters))
        {
            var idParameters = chunk.Select((_, index) => $"@Id{index}").ToArray();
            var parameters = chunk.Select((feature, index) => _Param($"Id{index}", feature.Id)).ToArray();
            var sql =
                $"""DELETE FROM {SqlServerFeaturesStorageInitializer.Qualified(storageOptions.Value, storageOptions.Value.FeatureValuesTableName)} WHERE [Id] IN ({string.Join(",", idParameters)});""";

            await _ExecuteAsync(sql, cancellationToken, parameters).ConfigureAwait(false);
        }

        foreach (var feature in features)
        {
            await _PublishAsync(feature, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<FeatureValueRecord>> _ReadValuesAsync(
        string sql,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters
    )
    {
        var result = new List<FeatureValueRecord>();
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _CommandTimeout(),
        };
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
        await using var connection = new SqlConnection(providerOptions.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _CommandTimeout(),
        };
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _PublishAsync(FeatureValueRecord feature, CancellationToken cancellationToken)
    {
        // Resolve the scoped publisher inside a fresh scope to avoid capturing it from the singleton's root provider.
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetService<ILocalMessagePublisher>();

        if (publisher is not null)
        {
            await publisher.PublishAsync(new EntityChangedEventData<FeatureValueRecord>(feature), cancellationToken).ConfigureAwait(false);
        }
    }

    private int _CommandTimeout() => (int)providerOptions.Value.CommandTimeout.TotalSeconds;

    private static SqlParameter _Param(string name, object? value) => new($"@{name}", value ?? DBNull.Value);
}
