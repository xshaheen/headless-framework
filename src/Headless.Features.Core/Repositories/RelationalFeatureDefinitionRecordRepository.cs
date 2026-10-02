// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Headless.Features.Entities;
using Headless.Primitives;
using Headless.Serializer;
using Headless.Sql;

namespace Headless.Features.Repositories;

/// <summary>
/// The relational <see cref="IFeatureDefinitionRecordRepository"/>, written once over <see cref="ISqlDialect"/>. New
/// groups and features are written with multi-row <c>INSERT</c> statements.
/// </summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalFeatureDefinitionRecordRepository(
    RelationalFeaturesTables tables,
    RelationalFeaturesOptions options,
    IJsonSerializer serializer
) : IFeatureDefinitionRecordRepository
{
    // SQL Server allows 2,100 parameters per statement: 100 feature rows use 1,100.
    private const int _MaxRowsPerInsert = 100;

    // Written text is unbounded: a parameter sized to the column would silently truncate an over-long value, where the
    // column must reject it.
    private static readonly SqlColumnType _Written = SqlColumnType.Text(-1);

    private static readonly string[] _FeatureColumns =
    [
        "Id",
        "GroupName",
        "Name",
        "ParentName",
        "DisplayName",
        "Description",
        "DefaultValue",
        "IsVisibleToClients",
        "IsAvailableToHost",
        "Providers",
        "ExtraProperties",
    ];

    private static readonly string[] _GroupColumns = ["Id", "Name", "DisplayName", "ExtraProperties"];

    private readonly ISqlDialect _dialect = tables.Dialect;
    private readonly string _selectFeatures = _Select(tables, tables.Definitions, _FeatureColumns);
    private readonly string _updateFeature = _Update(tables, tables.Definitions, _FeatureColumns);
    private readonly string _deleteFeature = _Delete(tables, tables.Definitions);
    private readonly string _selectGroups = _Select(tables, tables.Groups, _GroupColumns);
    private readonly string _updateGroup = _Update(tables, tables.Groups, _GroupColumns);
    private readonly string _deleteGroup = _Delete(tables, tables.Groups);
    private readonly ConcurrentDictionary<int, string> _insertFeatures = new();
    private readonly ConcurrentDictionary<int, string> _insertGroups = new();

    /// <inheritdoc/>
    public async Task<List<FeatureDefinitionRecord>> GetFeaturesListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<FeatureDefinitionRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, _selectFeatures);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new FeatureDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
                await reader.IsDBNullAsync(6, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                await reader.IsDBNullAsync(9, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(9)
            );

            _CopyExtraProperties(reader.GetString(10), record.ExtraProperties);
            result.Add(record);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<FeatureGroupDefinitionRecord>> GetGroupsListAsync(
        CancellationToken cancellationToken = default
    )
    {
        var result = new List<FeatureGroupDefinitionRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, _selectGroups);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new FeatureGroupDefinitionRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
            _CopyExtraProperties(reader.GetString(3), record.ExtraProperties);
            result.Add(record);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        List<FeatureGroupDefinitionRecord> newGroups,
        List<FeatureGroupDefinitionRecord> updatedGroups,
        List<FeatureGroupDefinitionRecord> deletedGroups,
        List<FeatureDefinitionRecord> newFeatures,
        List<FeatureDefinitionRecord> updatedFeatures,
        List<FeatureDefinitionRecord> deletedFeatures,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await _InsertManyAsync(
                connection,
                transaction,
                newGroups,
                _insertGroups,
                _InsertGroupsSql,
                _AddGroupParameters,
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var record in updatedGroups)
        {
            await using var command = _CreateCommand(connection, transaction, _updateGroup);
            _AddGroupParameters(command, record, rowIndex: null);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in deletedGroups)
        {
            await _DeleteAsync(connection, transaction, _deleteGroup, record.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        await _InsertManyAsync(
                connection,
                transaction,
                newFeatures,
                _insertFeatures,
                _InsertFeaturesSql,
                _AddFeatureParameters,
                cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var record in updatedFeatures)
        {
            await using var command = _CreateCommand(connection, transaction, _updateFeature);
            _AddFeatureParameters(command, record, rowIndex: null);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in deletedFeatures)
        {
            await _DeleteAsync(connection, transaction, _deleteFeature, record.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _InsertManyAsync<TRecord>(
        DbConnection connection,
        DbTransaction transaction,
        List<TRecord> records,
        ConcurrentDictionary<int, string> insertSqlByRowCount,
        Func<int, string> buildInsertSql,
        Action<DbCommand, TRecord, int?> addParameters,
        CancellationToken cancellationToken
    )
    {
        for (var offset = 0; offset < records.Count; offset += _MaxRowsPerInsert)
        {
            var rowCount = Math.Min(_MaxRowsPerInsert, records.Count - offset);
            await using var command = _CreateCommand(
                connection,
                transaction,
                insertSqlByRowCount.GetOrAdd(rowCount, buildInsertSql)
            );

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                addParameters(command, records[offset + rowIndex], rowIndex);
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task _DeleteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, transaction, sql);
        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // A null row index binds the single-row names (@Id); an index binds one row of a multi-row insert (@Id_3).
    private void _AddGroupParameters(DbCommand command, FeatureGroupDefinitionRecord record, int? rowIndex)
    {
        _dialect.AddParameter(command, _ParameterName("Id", rowIndex), SqlColumnType.Guid, record.Id);
        _dialect.AddParameter(command, _ParameterName("Name", rowIndex), _Written, record.Name);
        _dialect.AddParameter(command, _ParameterName("DisplayName", rowIndex), _Written, record.DisplayName);
        _AddExtraProperties(command, rowIndex, record.ExtraProperties);
    }

    private void _AddFeatureParameters(DbCommand command, FeatureDefinitionRecord record, int? rowIndex)
    {
        _dialect.AddParameter(command, _ParameterName("Id", rowIndex), SqlColumnType.Guid, record.Id);
        _dialect.AddParameter(command, _ParameterName("GroupName", rowIndex), _Written, record.GroupName);
        _dialect.AddParameter(command, _ParameterName("Name", rowIndex), _Written, record.Name);
        _dialect.AddParameter(command, _ParameterName("ParentName", rowIndex), _Written, record.ParentName);
        _dialect.AddParameter(command, _ParameterName("DisplayName", rowIndex), _Written, record.DisplayName);
        _dialect.AddParameter(command, _ParameterName("Description", rowIndex), _Written, record.Description);
        _dialect.AddParameter(command, _ParameterName("DefaultValue", rowIndex), _Written, record.DefaultValue);
        _dialect.AddParameter(
            command,
            _ParameterName("IsVisibleToClients", rowIndex),
            SqlColumnType.Boolean,
            record.IsVisibleToClients
        );
        _dialect.AddParameter(
            command,
            _ParameterName("IsAvailableToHost", rowIndex),
            SqlColumnType.Boolean,
            record.IsAvailableToHost
        );
        _dialect.AddParameter(command, _ParameterName("Providers", rowIndex), _Written, record.Providers);
        _AddExtraProperties(command, rowIndex, record.ExtraProperties);
    }

    private void _AddExtraProperties(DbCommand command, int? rowIndex, ExtraProperties extraProperties)
    {
        _dialect.AddParameter(
            command,
            _ParameterName("ExtraProperties", rowIndex),
            _Written,
            serializer.SerializeToString(extraProperties) ?? "{}"
        );
    }

    private void _CopyExtraProperties(string json, ExtraProperties target)
    {
        foreach (var (key, value) in serializer.Deserialize<ExtraProperties>(json) ?? [])
        {
            target[key] = value;
        }
    }

    private DbCommand _CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        return command;
    }

    private string _InsertGroupsSql(int rowCount) => _Insert(tables.Groups, _GroupColumns, rowCount);

    private string _InsertFeaturesSql(int rowCount) => _Insert(tables.Definitions, _FeatureColumns, rowCount);

    private string _Insert(string table, string[] columns, int rowCount)
    {
        var builder = new StringBuilder(128 + (rowCount * columns.Length * 20))
            .Append("INSERT INTO ")
            .Append(table)
            .Append(" (")
            .AppendJoin(", ", columns.Select(tables.Column))
            .Append(") VALUES ");

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            builder
                .Append(rowIndex > 0 ? ", (" : "(")
                .AppendJoin(", ", columns.Select(column => "@" + _ParameterName(column, rowIndex)))
                .Append(')');
        }

        return builder.Append(';').ToString();
    }

    private static string _ParameterName(string name, int? rowIndex)
    {
        return rowIndex is { } index ? string.Create(CultureInfo.InvariantCulture, $"{name}_{index}") : name;
    }

    private static string _Select(RelationalFeaturesTables t, string table, string[] columns)
    {
        return $"SELECT {string.Join(", ", columns.Select(t.Column))} FROM {table};";
    }

    private static string _Update(RelationalFeaturesTables t, string table, string[] columns)
    {
        return $"UPDATE {table} SET {string.Join(", ", columns.Skip(1).Select(c => $"{t.Column(c)} = @{c}"))} "
            + $"WHERE {t.Column("Id")} = @Id;";
    }

    private static string _Delete(RelationalFeaturesTables t, string table)
    {
        return $"DELETE FROM {table} WHERE {t.Column("Id")} = @Id;";
    }
}
#pragma warning restore CA2100
