// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Features.Entities;
using Headless.Sql;

namespace Headless.Features.Repositories;

/// <summary>
/// The relational <see cref="IFeatureValueRecordRepository"/>, written once over <see cref="ISqlDialect"/>. Batched
/// operations bind the whole list as one parameter, so the statement text does not grow with the list.
/// </summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalFeatureValueRecordRepository(
    RelationalFeaturesTables tables,
    RelationalFeaturesOptions options,
    TimeProvider timeProvider
) : IFeatureValueRecordRepository
{
    // Filters are sized to their columns, so the plan is reused and the comparison keeps the column's collation.
    private static readonly SqlColumnType _NameFilter = SqlColumnType.Text(FeatureValueRecordConstants.NameMaxLength);
    private static readonly SqlColumnType _ProviderNameFilter = SqlColumnType.Text(
        FeatureValueRecordConstants.ProviderNameMaxLength
    );
    private static readonly SqlColumnType _ProviderKeyFilter = SqlColumnType.Text(
        FeatureValueRecordConstants.ProviderKeyMaxLength
    );

    // Written text is unbounded: a parameter sized to the column would silently truncate an over-long value, where the
    // column must reject it.
    private static readonly SqlColumnType _Written = SqlColumnType.Text(-1);

    private readonly ISqlDialect _dialect = tables.Dialect;
    private readonly string _select = _BuildSelect(tables);
    private readonly string _byScope = _BuildByScope(tables);
    private readonly string _name = tables.Column("Name");
    private readonly string _providerName = tables.Column("ProviderName");
    private readonly string _providerKey = tables.Column("ProviderKey");
    private readonly string _insert = _BuildInsert(tables);
    private readonly string _update = _BuildUpdate(tables);
    private readonly string _delete =
        $"DELETE FROM {tables.Values} WHERE {tables.Dialect.InList(tables.Column("Id"), "Ids", SqlColumnType.Guid)};";

    /// <inheritdoc/>
    public async Task<FeatureValueRecord?> FindAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        // The unique indexes allow one row per scope; the order picks the same one if a schema lacks them.
        var sql =
            $"{_select} WHERE {_name} = @Name AND {_NullSafeEquals(_providerName, "ProviderName")} "
            + $"AND {_NullSafeEquals(_providerKey, "ProviderKey")} ORDER BY {tables.Column("Id")};";

        var rows = await _ReadAsync(
                sql,
                command =>
                {
                    _dialect.AddParameter(command, "Name", _NameFilter, name);
                    _dialect.AddParameter(command, "ProviderName", _ProviderNameFilter, providerName);
                    _dialect.AddParameter(command, "ProviderKey", _ProviderKeyFilter, providerKey);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return rows is [var row, ..] ? row : null;
    }

    /// <inheritdoc/>
    public Task<List<FeatureValueRecord>> FindAllAsync(
        string name,
        string? providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var filters = new List<string>(3) { $"{_name} = @Name" };

        if (providerName is not null)
        {
            filters.Add($"{_providerName} = @ProviderName");
        }

        if (providerKey is not null)
        {
            filters.Add($"{_providerKey} = @ProviderKey");
        }

        var sql = $"{_select} WHERE {string.Join(" AND ", filters)};";

        return _ReadAsync(
            sql,
            command =>
            {
                _dialect.AddParameter(command, "Name", _NameFilter, name);

                if (providerName is not null)
                {
                    _dialect.AddParameter(command, "ProviderName", _ProviderNameFilter, providerName);
                }

                if (providerKey is not null)
                {
                    _dialect.AddParameter(command, "ProviderKey", _ProviderKeyFilter, providerKey);
                }
            },
            cancellationToken
        );
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

        var sql = $"{_select} WHERE {_dialect.InList(_name, "Names", _NameFilter)} AND {_byScope};";

        return _ReadAsync(
            sql,
            command =>
            {
                _dialect.AddListParameter(command, "Names", _NameFilter, names);
                _AddScope(command, providerName, providerKey);
            },
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public Task<List<FeatureValueRecord>> GetListAsync(
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        return _ReadAsync(
            $"{_select} WHERE {_byScope};",
            command => _AddScope(command, providerName, providerKey),
            cancellationToken
        );
    }

    /// <inheritdoc/>
    public Task InsertAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        return _SaveAsync([feature], [], [], cancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(FeatureValueRecord feature, CancellationToken cancellationToken = default)
    {
        return _SaveAsync([], [feature], [], cancellationToken, requireUpdatedRows: false);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(
        IReadOnlyCollection<FeatureValueRecord> features,
        CancellationToken cancellationToken = default
    )
    {
        return _SaveAsync([], [], features, cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveAsync(
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken = default
    )
    {
        return _SaveAsync(inserted, updated, deleted, cancellationToken);
    }

    private async Task _SaveAsync(
        IReadOnlyCollection<FeatureValueRecord> inserted,
        IReadOnlyCollection<FeatureValueRecord> updated,
        IReadOnlyCollection<FeatureValueRecord> deleted,
        CancellationToken cancellationToken,
        bool requireUpdatedRows = true
    )
    {
        if (inserted.Count == 0 && updated.Count == 0 && deleted.Count == 0)
        {
            return;
        }

        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var feature in inserted)
        {
            await using var command = _CreateCommand(connection, transaction, _insert);
            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, feature.Id);
            _dialect.AddParameter(command, "Name", _Written, feature.Name);
            _dialect.AddParameter(command, "Value", _Written, feature.Value);
            _dialect.AddParameter(command, "ProviderName", _Written, feature.ProviderName);
            _dialect.AddParameter(command, "ProviderKey", _Written, feature.ProviderKey);
            // A caller-supplied CreatedAt is kept, as the EF path does, so pinned and backfilled timestamps round-trip;
            // only a default one is stamped now.
            _dialect.AddParameter(
                command,
                "CreatedAt",
                SqlColumnType.Timestamp,
                feature.CreatedAt == default ? timeProvider.GetUtcNow() : feature.CreatedAt
            );
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var feature in updated)
        {
            await using var command = _CreateCommand(connection, transaction, _update);
            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, feature.Id);
            _dialect.AddParameter(command, "Value", _Written, feature.Value);
            _dialect.AddParameter(
                command,
                "UpdatedAt",
                SqlColumnType.Timestamp,
                feature.UpdatedAt is { } updatedAt && updatedAt != default ? updatedAt : timeProvider.GetUtcNow()
            );
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            // An update that matched no row means another writer deleted it after the caller read it. Failing rolls
            // the batch back so the caller can re-read and retry, instead of silently dropping that value.
            if (affected == 0 && requireUpdatedRows)
            {
                throw new DBConcurrencyException("A value record in the batch was deleted by another writer.");
            }
        }

        if (deleted.Count != 0)
        {
            await using var command = _CreateCommand(connection, transaction, _delete);
            _dialect.AddListParameter(command, "Ids", SqlColumnType.Guid, deleted.Select(x => x.Id).ToList());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Disposing an uncommitted transaction rolls it back, so a statement that throws above undoes every earlier
        // one in the batch.
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void _AddScope(DbCommand command, string providerName, string? providerKey)
    {
        _dialect.AddParameter(command, "ProviderName", _ProviderNameFilter, providerName);
        _dialect.AddParameter(command, "ProviderKey", _ProviderKeyFilter, providerKey);
    }

    private async Task<List<FeatureValueRecord>> _ReadAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken
    )
    {
        var result = new List<FeatureValueRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, sql);
        bind(command);
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

    private DbCommand _CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        return command;
    }

    private static string _BuildSelect(RelationalFeaturesTables t)
    {
        return $"SELECT {t.Column("Id")}, {t.Column("Name")}, {t.Column("Value")}, {t.Column("ProviderName")}, "
            + $"{t.Column("ProviderKey")}, {t.Column("CreatedAt")}, {t.Column("UpdatedAt")} FROM {t.Values}";
    }

    private static string _BuildByScope(RelationalFeaturesTables t)
    {
        return $"{t.Column("ProviderName")} = @ProviderName AND {_NullSafeEquals(t.Column("ProviderKey"), "ProviderKey")}";
    }

    // Null-safe on both engines: a null parameter matches only the rows whose column is null.
    private static string _NullSafeEquals(string column, string parameter)
    {
        return $"(({column} IS NULL AND @{parameter} IS NULL) OR {column} = @{parameter})";
    }

    private static string _BuildInsert(RelationalFeaturesTables t)
    {
        return $"INSERT INTO {t.Values} ({t.Column("Id")}, {t.Column("Name")}, {t.Column("Value")}, "
            + $"{t.Column("ProviderName")}, {t.Column("ProviderKey")}, {t.Column("CreatedAt")}) "
            + "VALUES (@Id, @Name, @Value, @ProviderName, @ProviderKey, @CreatedAt);";
    }

    private static string _BuildUpdate(RelationalFeaturesTables t)
    {
        return $"UPDATE {t.Values} SET {t.Column("Value")} = @Value, {t.Column("UpdatedAt")} = @UpdatedAt "
            + $"WHERE {t.Column("Id")} = @Id;";
    }
}
#pragma warning restore CA2100
