// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Globalization;
using System.Runtime.InteropServices;
using Headless.Fencing;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

#pragma warning disable MA0048 // A topic file: the grant helper and the small records it reads and returns.
namespace Headless.Jobs.Infrastructure;

/// <summary>
/// A row a claim was granted: its key, the generation that fences its writes, when the grant expires, and how many
/// expired attempts the row's work has been taken from.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JobsGrantedRow(Guid Id, long Generation, DateTimeOffset ExpiresAt, int TakeoverCount);

/// <summary>
/// Grants job rows to a claim in one statement through <see cref="FencedRows.GrantScope{TKey}"/>: every row the
/// statement updates takes its own next generation, and the expiry is judged and stamped by the database clock.
/// </summary>
/// <remarks>
/// The statement writes the fence the scope leaves to its consumer, "no live attempt holds the row", inline on the
/// updated row, so a racer that blocks on the winner's row lock re-checks it against the winner's committed version
/// and matches nothing.
/// </remarks>
internal static class JobsLeaseGrant
{
    // Not "Lease…": FencedRows reserves that prefix for its own parameters.
    private const string _Prefix = "JobsGrant";

    private static readonly string _ActiveLiteral = ((short)RowLeaseState.Active).ToString(
        CultureInfo.InvariantCulture
    );

    /// <summary>
    /// Grants each row of <paramref name="ids"/> that no live attempt holds and that passes
    /// <paramref name="condition"/>, writing <paramref name="set"/> in the same statement.
    /// </summary>
    /// <param name="table">The descriptor of the rows' table.</param>
    /// <param name="context">The context whose connection and transaction the statement runs on.</param>
    /// <param name="set">Extra assignments, and the parameters <paramref name="condition"/> binds through it.</param>
    /// <param name="ids">The rows to grant.</param>
    /// <param name="condition">Trusted SQL over the row's unqualified columns, or <see langword="null"/>.</param>
    /// <param name="deadline">
    /// Trusted SQL for the expiry, such as a root's stored deadline, or <see langword="null"/> for the database clock
    /// plus <paramref name="duration"/>.
    /// </param>
    /// <param name="duration">How long a grant without a <paramref name="deadline"/> lasts.</param>
    /// <param name="holder">The claim's owner.</param>
    /// <param name="reclaimOwn">
    /// Whether a row <paramref name="holder"/> already holds may be granted again. The new generation fences off the
    /// earlier attempt, so a node re-picking its own rows never runs one twice.
    /// </param>
    /// <param name="cancellationToken">The token to observe.</param>
    /// <returns>The rows granted.</returns>
    public static async Task<List<JobsGrantedRow>> GrantAsync<TEntity>(
        FencedRowTable<Guid> table,
        DbContext context,
        JobsRowSet set,
        IReadOnlyCollection<Guid> ids,
        string? condition,
        string? deadline,
        TimeSpan duration,
        string holder,
        bool reclaimOwn,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var columns = Columns<TEntity>(context);
        var scope = Scope(table);
        var idList = string.Join(", ", ids.Select(id => set.Parameter(nameof(TimeJobEntity.Id), id)));
        var extra = set.ToWrite();
        // The scope returns key, generation and expiry; the takeover count the grant wrote rides along so the claimed
        // entity matches the stored lease.
        string[] returning = [.. scope.Returning, columns.TakeoverCount];
        var assignments = scope.Assignments(deadline);

        if (extra.Set is { } more)
        {
            assignments += ", " + more;
        }

        var notLive = $"NOT ({columns.State} = {_ActiveLiteral} AND {columns.ExpiresAt} > {SqlDialectTokens.Now})";
        var fence = reclaimOwn
            ? $"({notLive} OR ({columns.State} = {_ActiveLiteral} AND {columns.Holder} = @{scope.Prefix}Holder))"
            : notLive;
        var where =
            $"{columns.Id} IN ({idList}) AND {fence}" + (condition is null ? string.Empty : $" AND ({condition})");

        var dialect = table.Dialect;
        var sql =
            dialect is PostgreSqlDialect || JobsSqlDialects.IsSqlite(dialect)
                ? $"UPDATE {columns.Table} SET {assignments} WHERE {where} RETURNING {string.Join(", ", returning)};"
                : $"UPDATE {columns.Table} SET {assignments} OUTPUT {string.Join(", ", returning.Select(static c => "inserted." + c))} WHERE {where};";

        var connection = JobsLeaseRows.Connection(context);
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = JobsLeaseRows.Transaction(context);
#pragma warning disable CA2100 // SQL identifiers are provider-delimited EF metadata; runtime values are parameters.
            command.CommandText = dialect.Render(new SqlClockedStatement(sql));
#pragma warning restore CA2100
            scope.Bind(command, duration, holder);
            extra.Bind?.Invoke(command);

            var granted = new List<JobsGrantedRow>(ids.Count);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                granted.Add(
                    new JobsGrantedRow(
                        await reader.GetFieldValueAsync<Guid>(0, cancellationToken).ConfigureAwait(false),
                        await reader.GetFieldValueAsync<long>(1, cancellationToken).ConfigureAwait(false),
                        await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken).ConfigureAwait(false),
                        await reader.GetFieldValueAsync<int>(3, cancellationToken).ConfigureAwait(false)
                    )
                );
            }

            return granted;
        }
        finally
        {
            if (openedHere)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The grant scope every Jobs claim renders, so a native claim statement and this helper bind the same parameter
    /// names.
    /// </summary>
    public static FencedRowGrantScope<Guid> Scope(FencedRowTable<Guid> table) => FencedRows.GrantScope(table, _Prefix);

    /// <summary>
    /// The SQL fence that a row's attempt is still current: the row at <paramref name="idParameter"/> holds
    /// <paramref name="generationParameter"/>, is active, and has not expired by the database clock.
    /// </summary>
    public static string CurrentFence(
        JobsGrantColumns columns,
        string alias,
        string idParameter,
        string generationParameter
    )
    {
        return $"EXISTS (SELECT 1 FROM {columns.Table} AS {alias} WHERE {alias}.{columns.Id} = {idParameter} AND {alias}.{columns.Generation} = {generationParameter} AND {alias}.{columns.State} = {_ActiveLiteral} AND {alias}.{columns.ExpiresAt} > {SqlDialectTokens.Now})";
    }

    /// <summary>The stored deadline of the row at <paramref name="idParameter"/>, for a grant that expires with it.</summary>
    public static string StoredDeadline(JobsGrantColumns columns, string alias, string idParameter)
    {
        return $"(SELECT {alias}.{columns.ExpiresAt} FROM {columns.Table} AS {alias} WHERE {alias}.{columns.Id} = {idParameter})";
    }

    /// <summary>
    /// Sets the lease each node of a claimed tree was granted, so the run fences every node's writes on its own
    /// generation. The tree was loaded after the grant, but the walk's generations are the authority.
    /// </summary>
    public static void StampClaimedTree(
        TimeJobEntity node,
        IReadOnlyDictionary<Guid, long> claimed,
        DateTimeOffset expiresAt,
        string holder
    )
    {
        if (claimed.TryGetValue(node.Id, out var generation))
        {
            node.Lease = node.Lease with
            {
                Generation = generation,
                State = RowLeaseState.Active,
                ExpiresAt = expiresAt,
                Holder = holder,
            };
        }

        foreach (var child in node.Children)
        {
            StampClaimedTree(child, claimed, expiresAt, holder);
        }
    }

    /// <summary>The delimited table, key, and lease columns of <typeparamref name="TEntity"/>.</summary>
    public static JobsGrantColumns Columns<TEntity>(DbContext context)
        where TEntity : class
    {
        var entity =
            context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped by the Jobs DbContext.");
        var tableName =
            entity.GetTableName()
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped to a relational table.");
        var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        var lease = JobsLeaseColumns.Create(entity, store, sql);
        var id =
            entity.FindProperty(nameof(TimeJobEntity.Id))?.GetColumnName(store)
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.Id has no column mapping.");

        return new JobsGrantColumns(
            sql.DelimitIdentifier(tableName, entity.GetSchema()),
            sql.DelimitIdentifier(id),
            lease.Generation,
            lease.State,
            lease.ExpiresAt,
            lease.Holder,
            lease.TakeoverCount
        );
    }
}

/// <summary>The delimited columns a grant statement names.</summary>
internal sealed record JobsGrantColumns(
    string Table,
    string Id,
    string Generation,
    string State,
    string ExpiresAt,
    string Holder,
    string TakeoverCount
);
