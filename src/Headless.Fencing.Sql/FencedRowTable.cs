// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;
using Headless.Checks;
using Headless.Sql;

#pragma warning disable CA1000 // The factories name the key type, which no argument carries for inference to find.
namespace Headless.Fencing;

/// <summary>
/// A consumer's table whose rows each carry a lease in five columns: the table, its key, the lease columns, and where
/// generations come from. Build it once and keep it; every <see cref="FencedRows" /> operation takes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generations and key reuse.</b> Without a sequence, each row counts its own generations from the value it stores,
/// so a stale token is refused only while its row lives. Delete a row and insert another under the same key, and the
/// new row counts from 0 again: a worker still holding the old row's generation 3 is accepted once the new row reaches
/// generation 3. Never reuse a key while a token for it can still be in flight, or pass a sequence, whose values only
/// grow across the whole store.
/// </para>
/// <para>
/// <b>SQLite sequences.</b> SQLite has no sequence objects: the sequence there is a one-row table (<c>id</c>, <c>value</c>)
/// whose value a trigger on the generation column raises. One statement reads it once, so every row a single claim or
/// grant scope grants shares one generation. Each is still above that row's earlier generations, which is all the fence
/// needs.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The row's key type.</typeparam>
[PublicAPI]
public sealed class FencedRowTable<TKey>
    where TKey : notnull
{
    internal const string GenerationParameter = "LeaseGeneration";
    internal const string DurationParameter = "LeaseDuration";
    internal const string HolderParameter = "LeaseHolder";
    internal const string BatchSizeParameter = "LeaseBatchSize";
    internal const string ReservedPrefix = "Lease";

    // Variables a dialect declares inside the rendered transition (SQL Server's DECLARE @now and @applied); a consumer
    // parameter of the same name fails the batch's compilation.
    internal static readonly FrozenSet<string> DialectVariables = FrozenSet.ToFrozenSet(
        ["now", "applied"],
        StringComparer.OrdinalIgnoreCase
    );

    private FencedRowTable(
        ISqlDialect dialect,
        string? schema,
        string table,
        FencedRowKey<TKey> key,
        FencedRowColumns columns,
        string? generationSequence
    )
    {
        Dialect = dialect;
        Key = key;
        Name = schema is null ? table : schema + "." + table;
        Table = schema is null ? dialect.Quote(table) : dialect.Qualify(schema, table);
        Generation = dialect.Quote(columns.Generation);
        State = dialect.Quote(columns.State);
        ExpiresAt = dialect.Quote(columns.ExpiresAt);
        Holder = dialect.Quote(columns.Holder);
        TakeoverCount = dialect.Quote(columns.TakeoverCount);
        KeyColumns = [.. key.Parts.Select(p => dialect.Quote(p.Column))];
        KeyParameters = [.. KeyColumns.Select((column, i) => new SqlKeyColumn(column, _KeyParameter(i)))];
        NextGeneration = generationSequence is null
            ? $"{Generation} + 1"
            : dialect.NextSequenceValue(
                schema is null ? dialect.Quote(generationSequence) : dialect.Qualify(schema, generationSequence)
            );

        const string now = SqlDialectTokens.Now;
        var live = $"{State} = {ActiveLiteral} AND {ExpiresAt} > {now}";
        Current = $"{Generation} = @{GenerationParameter} AND {live}";
        GrantableFence = $"NOT ({live})";
        AfterSweepFence = $"{State} <> {ActiveLiteral}";

        LockedRead = dialect.Render(
            new SqlLockedRead(Table, KeyParameters, [Generation, State, ExpiresAt, TakeoverCount, Holder])
        );

        var deadline = dialect.ShiftByDuration(now, DurationParameter);
        var grant = GrantAssignments(deadline, HolderParameter);
        IReadOnlyList<string> granted = [Generation, ExpiresAt, TakeoverCount];

        GrantSql =
            LockedRead + dialect.Render(new SqlFencedTransition(Table, KeyParameters, GrantableFence, grant, granted));
        GrantAfterSweepSql =
            LockedRead + dialect.Render(new SqlFencedTransition(Table, KeyParameters, AfterSweepFence, grant, granted));
        RenewSql =
            LockedRead
            + dialect.Render(
                new SqlFencedTransition(Table, KeyParameters, Current, $"{ExpiresAt} = {deadline}", [ExpiresAt])
            );
        SettleSql = LockedRead + _Transition(Current, EndAssignments(RowLeaseState.Settled));
        ReleaseSql = LockedRead + _Transition(Current, EndAssignments(RowLeaseState.Released));
        // Revoke ends whatever attempt is active, live or expired, by key alone: the caller never held its generation.
        RevokeSql = LockedRead + _Transition($"{State} = {ActiveLiteral}", EndAssignments(RowLeaseState.Revoked));
        FenceSql = LockedRead + _Transition(Current, set: null);

        var byKey = string.Join(" AND ", KeyParameters.Select(static k => $"{k.Column} = @{k.Parameter}"));

        // Runs while this transaction holds the row a transition refused, so its clock judges the same row.
        LiveSql = dialect.Render(
            new SqlClockedStatement(
                $"SELECT CASE WHEN {ExpiresAt} > {now} THEN 1 ELSE 0 END FROM {Table} WHERE {byKey};"
            )
        );

        // No lock and no lock hint: a status read never waits on a grant or fence another transaction holds.
        StatusSql = dialect.Render(
            new SqlClockedStatement(
                $"SELECT {Generation}, {State}, CASE WHEN {State} = {ActiveLiteral} AND {ExpiresAt} > {now} THEN 1 ELSE 0 END "
                    + $"FROM {Table} WHERE {byKey};"
            )
        );

        AbandonSql = RenderAbandon(condition: null, set: null);
    }

    /// <summary>
    /// Gets the table's identity, <c>schema.table</c> unquoted (<c>table</c> alone for a table created without a
    /// schema), which every token carries and every operation checks, so a token from one table is never used on
    /// another.
    /// </summary>
    public string Name { get; }

    /// <summary>Gets the dialect the table's statements are rendered for.</summary>
    public ISqlDialect Dialect { get; }

    /// <summary>Gets how the table's key maps to its columns.</summary>
    public FencedRowKey<TKey> Key { get; }

    internal static string ActiveLiteral { get; } = _StateLiteral(RowLeaseState.Active);

    internal string Table { get; }

    internal string Generation { get; }

    internal string State { get; }

    internal string ExpiresAt { get; }

    internal string Holder { get; }

    internal string TakeoverCount { get; }

    internal IReadOnlyList<string> KeyColumns { get; }

    internal IReadOnlyList<SqlKeyColumn> KeyParameters { get; }

    internal string NextGeneration { get; }

    internal string Current { get; }

    internal string GrantableFence { get; }

    internal string AfterSweepFence { get; }

    internal string LockedRead { get; }

    internal string GrantSql { get; }

    internal string GrantAfterSweepSql { get; }

    internal string RenewSql { get; }

    internal string SettleSql { get; }

    internal string ReleaseSql { get; }

    internal string RevokeSql { get; }

    internal string FenceSql { get; }

    internal string LiveSql { get; }

    internal string StatusSql { get; }

    internal string AbandonSql { get; }

    /// <summary>Describes a table whose rows carry a lease.</summary>
    /// <param name="dialect">The table's database engine.</param>
    /// <param name="schema">
    /// The table's schema, unquoted, or <see langword="null" /> to name the table unqualified, so the database resolves it
    /// as it resolves any unqualified name (PostgreSQL's <c>search_path</c>, SQL Server's default schema). SQLite has no
    /// schemas, and its dialect joins a schema and a name with an underscore, so a SQLite table that is not named that way
    /// takes <see langword="null" />.
    /// </param>
    /// <param name="table">The table's name, unquoted.</param>
    /// <param name="key">How the key maps to columns.</param>
    /// <param name="columns">The lease columns' names, or <see langword="null" /> for the defaults.</param>
    /// <param name="generationSequence">
    /// The name, unquoted and in <paramref name="schema" /> (unqualified when it is <see langword="null" />), of a sequence that issues generations across the whole
    /// store, or <see langword="null" /> for each row to count its own. Read the remarks on key reuse before choosing
    /// <see langword="null" />.
    /// </param>
    /// <returns>The table descriptor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dialect" /> or <paramref name="key" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// A name, <paramref name="schema" /> included when it is not <see langword="null" />, is empty or white space, or two
    /// columns share a name.
    /// </exception>
    public static FencedRowTable<TKey> Create(
        ISqlDialect dialect,
        string? schema,
        string table,
        FencedRowKey<TKey> key,
        FencedRowColumns? columns = null,
        string? generationSequence = null
    )
    {
        Argument.IsNotNull(dialect);

        if (schema is not null)
        {
            Argument.IsNotNullOrWhiteSpace(schema);
        }

        Argument.IsNotNullOrWhiteSpace(table);
        Argument.IsNotNull(key);

        columns ??= new FencedRowColumns();
        string[] names =
        [
            Argument.IsNotNullOrWhiteSpace(columns.Generation, paramName: nameof(columns)),
            Argument.IsNotNullOrWhiteSpace(columns.State, paramName: nameof(columns)),
            Argument.IsNotNullOrWhiteSpace(columns.ExpiresAt, paramName: nameof(columns)),
            Argument.IsNotNullOrWhiteSpace(columns.Holder, paramName: nameof(columns)),
            Argument.IsNotNullOrWhiteSpace(columns.TakeoverCount, paramName: nameof(columns)),
        ];

        if (
            new HashSet<string>(names.Concat(key.Parts.Select(static p => p.Column)), StringComparer.Ordinal).Count
            != names.Length + key.Parts.Count
        )
        {
            throw new ArgumentException("Every lease column and key column needs its own name.", nameof(columns));
        }

        if (generationSequence is not null)
        {
            Argument.IsNotNullOrWhiteSpace(generationSequence);
        }

        return new(dialect, schema, table, key, columns, generationSequence);
    }

    /// <summary>
    /// The assignments that grant a row to a new attempt: the next generation, active, the deadline, the holder, and one
    /// more takeover when the row's last attempt was still active (and so expired).
    /// </summary>
    internal string GrantAssignments(string deadline, string holderParameter)
    {
        return $"""
            {Generation} = {NextGeneration},
                {State} = {ActiveLiteral},
                {ExpiresAt} = {deadline},
                {Holder} = @{holderParameter},
                {TakeoverCount} = CASE WHEN {State} = {ActiveLiteral} THEN {TakeoverCount} + 1 ELSE {TakeoverCount} END
            """;
    }

    /// <summary>
    /// Ends the attempt in <paramref name="state" />. Its takeovers belong to finished work, so the next grant counts
    /// from zero; the expiry and holder stay as a record of the last attempt.
    /// </summary>
    internal string EndAssignments(RowLeaseState state)
    {
        return $"{State} = {_StateLiteral(state)}, {TakeoverCount} = 0";
    }

    internal void BindKey(DbCommand command, TKey key)
    {
        var parts = Key.Parts;

        for (var i = 0; i < parts.Count; i++)
        {
            Dialect.AddParameter(command, KeyParameters[i].Parameter, parts[i].Type, parts[i].Value(key));
        }
    }

    /// <summary>
    /// Revokes the active attempt and applies <paramref name="set" /> in the same statement, fenced on the active state
    /// alone, as <see cref="RevokeSql" /> is.
    /// </summary>
    internal string RenderRevoke(string set)
    {
        return RenderTransition(
            $"{State} = {ActiveLiteral}",
            EndAssignments(RowLeaseState.Revoked) + ", " + set,
            [Generation]
        );
    }

    /// <summary>
    /// The sweep of expired active attempts, narrowed by <paramref name="condition" /> and applying
    /// <paramref name="set" /> beside the abandonment, both optional.
    /// </summary>
    internal string RenderAbandon(string? condition, string? set)
    {
        const string now = SqlDialectTokens.Now;
        var filter = $"{State} = {ActiveLiteral} AND {ExpiresAt} <= {now}";
        var assignments = $"{State} = {_StateLiteral(RowLeaseState.Abandoned)}, {TakeoverCount} = {TakeoverCount} + 1";

        // A sweep skips rows another transaction holds, so it never waits on a live fence or a concurrent sweeper.
        return Dialect.Render(
            new SqlClaimNext(
                Table,
                KeyColumns,
                condition is null ? filter : $"{filter} AND ({condition})",
                [ExpiresAt],
                set is null ? assignments : assignments + ", " + set,
                [Generation],
                BatchSizeParameter
            )
        );
    }

    /// <summary>
    /// Revokes up to the batch size of active attempts held by any of <paramref name="holderCount" /> holders, bound as
    /// <c>@LeaseHolder0</c> onward, skipping rows another transaction holds, narrowed by <paramref name="condition" /> and
    /// applying <paramref name="set" /> beside the revocation, both optional.
    /// </summary>
    internal string RenderRevokeHeldBy(int holderCount, string? condition, string? set)
    {
        var holders = string.Join(
            ", ",
            Enumerable.Range(0, holderCount).Select(static i => "@" + HolderListParameter(i))
        );
        var filter = $"{State} = {ActiveLiteral} AND {Holder} IN ({holders})";
        var assignments = EndAssignments(RowLeaseState.Revoked);

        return Dialect.Render(
            new SqlClaimNext(
                Table,
                KeyColumns,
                condition is null ? filter : $"{filter} AND ({condition})",
                [ExpiresAt],
                set is null ? assignments : assignments + ", " + set,
                [Generation],
                BatchSizeParameter
            )
        );
    }

    internal static string HolderListParameter(int index) =>
        HolderParameter + index.ToString(CultureInfo.InvariantCulture);

    internal string RenderTransition(string fence, string? set, IReadOnlyList<string> returning)
    {
        return LockedRead + Dialect.Render(new SqlFencedTransition(Table, KeyParameters, fence, set, returning));
    }

    private string _Transition(string fence, string? set)
    {
        // PostgreSQL's RETURNING needs at least one column, and the generation is always there to read.
        return Dialect.Render(new SqlFencedTransition(Table, KeyParameters, fence, set, [Generation]));
    }

    private static string _KeyParameter(int index) => "LeaseKey" + index.ToString(CultureInfo.InvariantCulture);

    private static string _StateLiteral(RowLeaseState state) => ((short)state).ToString(CultureInfo.InvariantCulture);
}
