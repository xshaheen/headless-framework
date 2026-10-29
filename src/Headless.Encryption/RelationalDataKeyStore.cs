// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Encryption;

/// <summary>
/// The one relational data-key store, written once against <see cref="ISqlDialect" />: plain reads, an
/// insert-if-absent keyed by (scope, kind, version) so racing creators agree on one key, a keyed update for re-wrap,
/// and a scope delete for shredding. Every call is autonomous: its own connection and short transaction.
/// </summary>
#pragma warning disable CA2100 // SQL text comes from the dialect over validated schema and table names; values are parameters.
internal sealed class RelationalDataKeyStore : IDataKeyStore
{
    private readonly ISqlDialect _dialect;
    private readonly RelationalEncryptionKeyStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _getSql;
    private readonly string _getActiveSql;
    private readonly string _listScopeSql;
    private readonly string _listFirstSql;
    private readonly string _listAfterSql;
    private readonly string _insertSql;
    private readonly string _replaceSql;
    private readonly string _deleteScopeSql;

    public RelationalDataKeyStore(
        ISqlDialect dialect,
        RelationalEncryptionKeyStoreOptions options,
        TimeProvider timeProvider
    )
    {
        _dialect = Argument.IsNotNull(dialect);
        _options = Argument.IsNotNull(options);
        _timeProvider = Argument.IsNotNull(timeProvider);

        var table = dialect.Qualify(options.Schema, options.TableName);
        var c = new DataKeyColumns(dialect);
        var select =
            $"SELECT {c.KeyId}, {c.Scope}, {c.Kind}, {c.Version}, {c.KekProvider}, {c.KekId}, {c.WrappedKey}, {c.CreatedAt} FROM {table}";

        _getSql = $"{select} WHERE {c.KeyId} = @KeyId;";
        _getActiveSql =
            $"{select} WHERE {c.Scope} = @Scope AND {c.Kind} = @Kind ORDER BY {c.Version} DESC {dialect.Limit("Limit")};";
        _listScopeSql = $"{select} WHERE {c.Scope} = @Scope;";
        _listFirstSql = $"{select} ORDER BY {c.KeyId} {dialect.Limit("Limit")};";
        _listAfterSql = $"{select} WHERE {c.KeyId} > @After ORDER BY {c.KeyId} {dialect.Limit("Limit")};";
        _insertSql = dialect.Render(
            new SqlInsertIfAbsent(
                table,
                [new(c.Scope, "Scope"), new(c.Kind, "Kind"), new(c.Version, "Version")],
                [c.KeyId, c.KekProvider, c.KekId, c.WrappedKey, c.CreatedAt],
                ["@KeyId", "@KekProvider", "@KekId", "@WrappedKey", SqlDialectTokens.Now],
                [c.KeyId]
            )
        );
        _replaceSql =
            $"UPDATE {table} SET {c.KekProvider} = @KekProvider, {c.KekId} = @KekId, {c.WrappedKey} = @WrappedKey WHERE {c.KeyId} = @KeyId;";
        _deleteScopeSql = $"DELETE FROM {table} WHERE {c.Scope} = @Scope;";
    }

    public Task<StoredDataKey?> GetAsync(Guid keyId, CancellationToken cancellationToken)
    {
        return _RunAsync(
            "encryption.get",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(connection, transaction, _getSql);
                _dialect.AddParameter(command, "KeyId", SqlColumnType.Guid, keyId);

                return await _ReadSingleAsync(command, ct).ConfigureAwait(false);
            },
            cancellationToken
        );
    }

    public Task<StoredDataKey?> GetActiveAsync(string scope, DataKeyKind kind, CancellationToken cancellationToken)
    {
        return _RunAsync(
            "encryption.get_active",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(connection, transaction, _getActiveSql);
                _AddScope(command, scope);
                _dialect.AddParameter(command, "Kind", SqlColumnType.Int16, (short)kind);
                _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, 1);

                return await _ReadSingleAsync(command, ct).ConfigureAwait(false);
            },
            cancellationToken
        );
    }

    public Task<IReadOnlyList<StoredDataKey>> ListScopeAsync(string scope, CancellationToken cancellationToken)
    {
        return _RunAsync(
            "encryption.list_scope",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(connection, transaction, _listScopeSql);
                _AddScope(command, scope);

                return await _ReadAllAsync(command, ct).ConfigureAwait(false);
            },
            cancellationToken
        );
    }

    public Task<IReadOnlyList<StoredDataKey>> ListAsync(
        Guid? afterKeyId,
        int limit,
        CancellationToken cancellationToken
    )
    {
        Argument.IsPositive(limit);

        return _RunAsync(
            "encryption.list",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(
                    connection,
                    transaction,
                    afterKeyId is null ? _listFirstSql : _listAfterSql
                );

                if (afterKeyId is { } after)
                {
                    _dialect.AddParameter(command, "After", SqlColumnType.Guid, after);
                }

                _dialect.AddParameter(command, "Limit", SqlColumnType.Int32, limit);

                return await _ReadAllAsync(command, ct).ConfigureAwait(false);
            },
            cancellationToken
        );
    }

    public Task<bool> TryInsertAsync(StoredDataKey key, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(key);
        Argument.IsPositive(key.Version);
        Argument.HasMaxLength(key.KekProvider, DataKeyFieldLimits.KekProviderMaxLength);
        Argument.HasMaxLength(key.WrappedKey.KekId, DataKeyFieldLimits.KekIdMaxLength);

        return _RunAsync(
            "encryption.insert",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(connection, transaction, _insertSql);
                _AddScope(command, key.Scope);
                _dialect.AddParameter(command, "Kind", SqlColumnType.Int16, (short)key.Kind);
                _dialect.AddParameter(command, "Version", SqlColumnType.Int32, key.Version);
                _dialect.AddParameter(command, "KeyId", SqlColumnType.Guid, key.KeyId);
                _AddWrapped(command, key.KekProvider, key.WrappedKey);

                var inserted = await SqlFencedCommand
                    .ExecuteAsync(
                        command,
                        lockedRead: false,
                        static (_, _) => ValueTask.FromResult(new object()),
                        static (_, _) => ValueTask.FromResult(true),
                        ct
                    )
                    .ConfigureAwait(false);

                return inserted.IsAccepted;
            },
            cancellationToken
        );
    }

    public Task<bool> ReplaceWrappedKeyAsync(
        Guid keyId,
        string kekProvider,
        WrappedDataKey wrappedKey,
        CancellationToken cancellationToken
    )
    {
        Argument.HasMaxLength(kekProvider, DataKeyFieldLimits.KekProviderMaxLength);
        Argument.IsNotNull(wrappedKey);
        Argument.HasMaxLength(wrappedKey.KekId, DataKeyFieldLimits.KekIdMaxLength);

        return _RunAsync(
            "encryption.rewrap",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(connection, transaction, _replaceSql);
                _dialect.AddParameter(command, "KeyId", SqlColumnType.Guid, keyId);
                _AddWrapped(command, kekProvider, wrappedKey);

                return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
            },
            cancellationToken
        );
    }

    public Task<int> DeleteScopeAsync(string scope, CancellationToken cancellationToken)
    {
        return _RunAsync(
            "encryption.shred",
            async (connection, transaction, ct) =>
            {
                await using var command = _Command(connection, transaction, _deleteScopeSql);
                _AddScope(command, scope);

                return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancellationToken
        );
    }

    private async Task<T> _RunAsync<T>(
        string operation,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> body,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await SqlAutonomousTransaction
                .RunAsync(
                    operation,
                    () => _dialect.CreateConnection(_options.ConnectionString),
                    body,
                    _timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        // SqlClient reports a command it cancelled mid-flight as a provider error; surface the cancellation asked for.
        catch (DbException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
    }

    private DbCommand _Command(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;

        return command;
    }

    private void _AddScope(DbCommand command, string scope)
    {
        _dialect.AddParameter(
            command,
            "Scope",
            SqlColumnType.KeyText(DataKeyFieldLimits.ScopeMaxLength),
            Argument.IsNotNull(scope)
        );
    }

    private void _AddWrapped(DbCommand command, string kekProvider, WrappedDataKey wrappedKey)
    {
        _dialect.AddParameter(
            command,
            "KekProvider",
            SqlColumnType.Text(DataKeyFieldLimits.KekProviderMaxLength),
            kekProvider
        );
        _dialect.AddParameter(
            command,
            "KekId",
            SqlColumnType.Text(DataKeyFieldLimits.KekIdMaxLength),
            wrappedKey.KekId
        );
        _dialect.AddParameter(command, "WrappedKey", SqlColumnType.Binary, wrappedKey.Ciphertext);
    }

    private static async Task<StoredDataKey?> _ReadSingleAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? await _ReadAsync(reader, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private static async Task<IReadOnlyList<StoredDataKey>> _ReadAllAsync(
        DbCommand command,
        CancellationToken cancellationToken
    )
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var keys = new List<StoredDataKey>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(await _ReadAsync(reader, cancellationToken).ConfigureAwait(false));
        }

        return keys;
    }

    private static async Task<StoredDataKey> _ReadAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        return new StoredDataKey
        {
            KeyId = reader.GetGuid(0),
            Scope = reader.GetString(1),
            Kind = (DataKeyKind)reader.GetInt16(2),
            Version = reader.GetInt32(3),
            KekProvider = reader.GetString(4),
            WrappedKey = new WrappedDataKey
            {
                KekId = reader.GetString(5),
                Ciphertext = await reader.GetFieldValueAsync<byte[]>(6, cancellationToken).ConfigureAwait(false),
            },
            CreatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(7, cancellationToken).ConfigureAwait(false),
        };
    }
}
#pragma warning restore CA2100

/// <summary>The key table's columns, in each dialect's naming convention and quoted.</summary>
internal sealed class DataKeyColumns(ISqlDialect dialect)
{
    public string KeyId { get; } = _Column(dialect, "KeyId");

    public string Scope { get; } = _Column(dialect, "Scope");

    public string Kind { get; } = _Column(dialect, "Kind");

    public string Version { get; } = _Column(dialect, "Version");

    public string KekProvider { get; } = _Column(dialect, "KekProvider");

    public string KekId { get; } = _Column(dialect, "KekId");

    public string WrappedKey { get; } = _Column(dialect, "WrappedKey");

    public string CreatedAt { get; } = _Column(dialect, "CreatedAt");

    private static string _Column(ISqlDialect dialect, string pascalName) => dialect.Quote(dialect.Name(pascalName));
}
