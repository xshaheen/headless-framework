// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// The one relational counter store: every call is a single <see cref="SqlUpsert" /> that creates the row on first
/// use and adds the step on every later use, returning the value it wrote. The dialect supplies the engine SQL.
/// </summary>
/// <remarks>
/// Concurrent first calls on one new key need no retry: the upsert serializes them on the key (PostgreSQL's unique
/// index, SQL Server's key-range lock), and the loser takes the update branch. The row lock is held until the
/// surrounding transaction ends, which is what makes a gap-free counter serialize its writers until commit.
/// </remarks>
#pragma warning disable CA2100 // SQL text comes from the dialect over validated schema and table names.
internal sealed class RelationalSequenceStore(
    ISqlDialect dialect,
    RelationalSequencesOptions options,
    string packageName,
    TimeProvider timeProvider
) : ISequenceStore
{
    private const string _Operation = "gap-free sequence";

    private readonly ISqlDialect _dialect = Argument.IsNotNull(dialect);
    private readonly RelationalSequencesOptions _options = Argument.IsNotNull(options);
    private readonly string _packageName = Argument.IsNotNullOrWhiteSpace(packageName);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly string _incrementSql = _BuildIncrementSql(dialect, options);

    public ValueTask<long> IncrementAsync(
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    )
    {
        // Each attempt opens its own connection and READ COMMITTED transaction, so a failed attempt's rolled-back
        // transaction is already gone when the retry starts. A stricter server default would turn a concurrent first
        // use into a serialization failure.
        return SqlAutonomousTransaction.RunAsync(
            () => _dialect.CreateConnection(_options.ConnectionString),
            (connection, transaction, ct) => _ExecuteAsync(connection, transaction, key, insertValue, delta, ct),
            _timeProvider,
            cancellationToken
        );
    }

    public void ValidateEnlistment(IUnitOfWork unitOfWork)
    {
        var (connection, _) = _RequireLive(unitOfWork);

        using var configured = _dialect.CreateConnection(_options.ConnectionString);
        RelationalEnlistment.RequireSameDatabase(configured, connection, _packageName, _Operation);
    }

    public async ValueTask<long> IncrementEnlistedAsync(
        IUnitOfWork unitOfWork,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken = default
    )
    {
        // Re-checked here rather than trusted from validation: the caller may have ended the transaction or closed the
        // connection in between. No retry: a deadlock has already rolled back the caller's transaction, so only the
        // unit's owner can decide whether to run the whole unit again.
        var (connection, transaction) = _RequireLive(unitOfWork);

        return await _ExecuteAsync(connection, transaction, key, insertValue, delta, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<long> _ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        SequenceKey key,
        long insertValue,
        long delta,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = _incrementSql;
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        _dialect.AddParameter(
            command,
            "TenantId",
            SqlColumnType.KeyText(SequenceFieldLimits.TenantIdMaxLength),
            key.TenantId
        );
        _dialect.AddParameter(command, "Name", SqlColumnType.KeyText(SequenceFieldLimits.NameMaxLength), key.Name);
        _dialect.AddParameter(
            command,
            "Partition",
            SqlColumnType.KeyText(SequenceFieldLimits.PartitionMaxLength),
            key.Partition
        );
        _dialect.AddParameter(command, "InsertValue", SqlColumnType.Int64, insertValue);
        _dialect.AddParameter(command, "Delta", SqlColumnType.Int64, delta);

        var upserted = await SqlUpsertCommand
            .ExecuteAsync(command, static (reader, _) => ValueTask.FromResult(reader.GetInt64(1)), cancellationToken)
            .ConfigureAwait(false);

        // The upsert has no guard, so it always writes.
        return upserted.Match(
            static (value, _) => value,
            static () => throw new InvalidOperationException("An unguarded counter upsert reported a refusal.")
        );
    }

    private (DbConnection Connection, DbTransaction Transaction) _RequireLive(IUnitOfWork unitOfWork)
    {
        return RelationalEnlistment.RequireLive(
            Argument.IsNotNull(unitOfWork),
            _dialect.ConnectionType,
            _dialect.TransactionType,
            _packageName,
            _Operation
        );
    }

    private static string _BuildIncrementSql(ISqlDialect dialect, RelationalSequencesOptions options)
    {
        var tenantId = SequencesColumns.TenantId(dialect);
        var name = SequencesColumns.Name(dialect);
        var partition = SequencesColumns.Partition(dialect);
        var value = SequencesColumns.Value(dialect);
        var createdAt = SequencesColumns.CreatedAt(dialect);
        var updatedAt = SequencesColumns.UpdatedAt(dialect);

        // The database clock stamps the write itself: a gap-free unit can hold its transaction open for a while.
        return dialect.Render(
            new SqlUpsert(
                dialect.Qualify(options.Schema, options.TableName),
                [new(tenantId, "TenantId"), new(name, "Name"), new(partition, "Partition")],
                [value, createdAt, updatedAt],
                ["@InsertValue", SqlDialectTokens.Now, SqlDialectTokens.Now],
                $"{value} = {SqlDialectTokens.Stored}.{value} + @Delta, {updatedAt} = {SqlDialectTokens.Now}",
                Guard: null,
                [value]
            )
        );
    }
}
#pragma warning restore CA2100

/// <summary>The counter table's columns, in each dialect's naming convention and quoted.</summary>
internal static class SequencesColumns
{
    public static string TenantId(ISqlDialect dialect) => _Column(dialect, "TenantId");

    public static string Name(ISqlDialect dialect) => _Column(dialect, "Name");

    // Quoted everywhere: PARTITION is a keyword in PostgreSQL's grammar.
    public static string Partition(ISqlDialect dialect) => _Column(dialect, "Partition");

    public static string Value(ISqlDialect dialect) => _Column(dialect, "Value");

    public static string CreatedAt(ISqlDialect dialect) => _Column(dialect, "CreatedAt");

    public static string UpdatedAt(ISqlDialect dialect) => _Column(dialect, "UpdatedAt");

    private static string _Column(ISqlDialect dialect, string pascalName) => dialect.Quote(dialect.Name(pascalName));
}
