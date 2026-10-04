// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Primitives;
using Headless.Serializer;
using Headless.Sql;

namespace Headless.Settings;

/// <summary>The relational <see cref="ISettingDefinitionRecordRepository"/>, written once over <see cref="ISqlDialect"/>.</summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalSettingDefinitionRecordRepository(
    RelationalSettingsTables tables,
    RelationalSettingsOptions options,
    IJsonSerializer serializer
) : ISettingDefinitionRecordRepository
{
    // Written text is unbounded: a parameter sized to the column would silently truncate an over-long value, where the
    // column must reject it.
    private static readonly SqlColumnType _Written = SqlColumnType.Text(-1);

    private static readonly string[] _Columns =
    [
        "Id",
        "Name",
        "DisplayName",
        "Description",
        "DefaultValue",
        "Providers",
        "IsVisibleToClients",
        "IsInherited",
        "IsEncrypted",
        "ExtraProperties",
    ];

    private readonly ISqlDialect _dialect = tables.Dialect;

    private readonly string _select =
        $"SELECT {string.Join(", ", _Columns.Select(tables.Column))} FROM {tables.Definitions};";

    private readonly string _insert =
        $"INSERT INTO {tables.Definitions} ({string.Join(", ", _Columns.Select(tables.Column))}) "
        + $"VALUES ({string.Join(", ", _Columns.Select(static c => "@" + c))});";

    private readonly string _update =
        $"UPDATE {tables.Definitions} SET "
        + string.Join(", ", _Columns.Skip(1).Select(c => $"{tables.Column(c)} = @{c}"))
        + $" WHERE {tables.Column("Id")} = @Id;";

    private readonly string _delete = $"DELETE FROM {tables.Definitions} WHERE {tables.Column("Id")} = @Id;";

    /// <inheritdoc/>
    public async Task<List<SettingDefinitionRecord>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SettingDefinitionRecord>();
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = _CreateCommand(connection, transaction: null, _select);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = new SettingDefinitionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3),
                await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)
            );

            foreach (var (key, value) in serializer.Deserialize<ExtraProperties>(reader.GetString(9)) ?? [])
            {
                record.ExtraProperties[key] = value;
            }

            result.Add(record);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        List<SettingDefinitionRecord> addedRecords,
        List<SettingDefinitionRecord> changedRecords,
        List<SettingDefinitionRecord> deletedRecords,
        CancellationToken cancellationToken = default
    )
    {
        await using var connection = _dialect.CreateConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in addedRecords)
        {
            await _WriteAsync(connection, transaction, _insert, record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in changedRecords)
        {
            await _WriteAsync(connection, transaction, _update, record, cancellationToken).ConfigureAwait(false);
        }

        foreach (var record in deletedRecords)
        {
            await using var command = _CreateCommand(connection, transaction, _delete);
            _dialect.AddParameter(command, "Id", SqlColumnType.Guid, record.Id);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task _WriteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        SettingDefinitionRecord record,
        CancellationToken cancellationToken
    )
    {
        await using var command = _CreateCommand(connection, transaction, sql);
        _dialect.AddParameter(command, "Id", SqlColumnType.Guid, record.Id);
        _dialect.AddParameter(command, "Name", _Written, record.Name);
        _dialect.AddParameter(command, "DisplayName", _Written, record.DisplayName);
        _dialect.AddParameter(command, "Description", _Written, record.Description);
        _dialect.AddParameter(command, "DefaultValue", _Written, record.DefaultValue);
        _dialect.AddParameter(command, "Providers", _Written, record.Providers);
        _dialect.AddParameter(command, "IsVisibleToClients", SqlColumnType.Boolean, record.IsVisibleToClients);
        _dialect.AddParameter(command, "IsInherited", SqlColumnType.Boolean, record.IsInherited);
        _dialect.AddParameter(command, "IsEncrypted", SqlColumnType.Boolean, record.IsEncrypted);
        _dialect.AddParameter(
            command,
            "ExtraProperties",
            _Written,
            serializer.SerializeToString(record.ExtraProperties) ?? "{}"
        );
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand _CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        return command;
    }
}
#pragma warning restore CA2100
