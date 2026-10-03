// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.InteropServices;

namespace Headless.Sql;

/// <summary>
/// Everything a relational store needs to know about one database engine, so the store itself is written once:
/// identifier naming and quoting, parameters, time arithmetic, the statement shapes that lock, fence, claim, and
/// purge rows, and error classification. Schema objects are not its concern: each feature contributes its DDL to the
/// schema runner.
/// </summary>
/// <remarks>
/// <para>
/// Statement fragments a store hands to the dialect name columns unqualified and write the database clock as
/// <see cref="SqlDialectTokens.Now" />. A locked read, fenced transition, claim, and batch lock read that clock only
/// after the rows they decide on are locked. <see cref="SqlUpsert" /> and <see cref="SqlInsertIfAbsent" /> may read it
/// before waiting on a concurrent writer of the same key, so a value they stamp can be early by that wait: decide a
/// lease or a due time with a locked read followed by a fenced transition, never with an upsert guard that compares
/// against the clock.
/// </para>
/// <para>
/// No rendered statement uses <c>TRY/CATCH</c> or a session <c>SET</c>, and no statement can
/// raise a duplicate-key error on the key it guards: a store runs them inside a caller's transaction, where a caught
/// error could still doom the transaction and a <c>SET</c> would outlive the statement.
/// </para>
/// </remarks>
[PublicAPI]
public interface ISqlDialect
{
    /// <summary>Gets the engine's display name, for messages.</summary>
    string DisplayName { get; }

    /// <summary>Gets the finest instant the engine stores; coarser engines round every stored instant to it.</summary>
    TimeSpan TimestampPrecision { get; }

    /// <summary>Gets the connection type the dialect's statements and parameters are written for.</summary>
    Type ConnectionType { get; }

    /// <summary>Gets the transaction type an enlisted statement must run under.</summary>
    Type TransactionType { get; }

    /// <summary>Creates an unopened connection of <see cref="ConnectionType" />.</summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>Returns an identifier in the engine's naming convention (snake_case or PascalCase).</summary>
    /// <param name="pascalName">The name in PascalCase; underscores separate prefixes such as <c>PK_</c>.</param>
    string Name(string pascalName);

    /// <summary>Quotes an identifier already in the engine's naming convention.</summary>
    string Quote(string identifier);

    /// <summary>Returns the quoted, schema-qualified name of a table or sequence.</summary>
    string Qualify(string schema, string identifier);

    /// <summary>Returns an expression that draws the next value of <paramref name="qualifiedSequence" />.</summary>
    string NextSequenceValue(string qualifiedSequence);

    /// <summary>
    /// Returns the literal of <paramref name="value" /> that compares with a boolean column: <c>TRUE</c> or
    /// <c>FALSE</c> on PostgreSQL, <c>1</c> or <c>0</c> on SQL Server.
    /// </summary>
    /// <remarks>
    /// A literal rather than a parameter, so a filtered index whose filter names the same literal stays usable: SQL
    /// Server never matches a parameterized predicate to an index filter.
    /// </remarks>
    string BooleanLiteral(bool value);

    /// <summary>Returns an expression that draws a new random <see cref="SqlColumnKind.Guid" /> for each row it is evaluated for.</summary>
    string NewGuid();

    /// <summary>
    /// Returns <paramref name="instant" /> moved forward by <paramref name="seconds" />, an integer expression such as
    /// a column, which must fit a 32-bit integer.
    /// </summary>
    string ShiftBySeconds(string instant, string seconds);

    /// <summary>
    /// Returns the clause that follows an <c>ORDER BY</c> and keeps at most <c>@</c><paramref name="limitParameter" />
    /// rows, after skipping <c>@</c><paramref name="offsetParameter" /> rows when one is given.
    /// </summary>
    /// <remarks>The statement must order its rows: SQL Server accepts the clause only after an <c>ORDER BY</c>.</remarks>
    string Limit(string limitParameter, string? offsetParameter = null);

    /// <summary>
    /// Returns a predicate that <paramref name="expression" /> matches the <c>LIKE</c> pattern bound under
    /// <paramref name="patternParameter" />, whose escape character is a backslash, without regard to case.
    /// </summary>
    /// <remarks>
    /// PostgreSQL matches with <c>ILIKE</c>. SQL Server's <c>LIKE</c> follows the column's collation, which ignores case
    /// under the default collations.
    /// </remarks>
    string LikeIgnoringCase(string expression, string patternParameter);

    /// <summary>
    /// Returns a reference to <paramref name="table" /> for a plain read, such as a dashboard query, that never waits on
    /// a row another transaction has locked.
    /// </summary>
    /// <remarks>
    /// PostgreSQL never blocks a plain read and returns a locked row as last committed. SQL Server skips a locked row
    /// (<c>READPAST</c>), with or without read committed snapshot isolation, so the read can miss a row being written.
    /// </remarks>
    string ReadWithoutWaiting(string table);

    /// <summary>
    /// Returns <paramref name="instant" /> moved by the duration bound with <see cref="AddDuration" /> under
    /// <paramref name="parameter" />, backward when <paramref name="subtract" /> is set.
    /// </summary>
    string ShiftByDuration(string instant, string parameter, bool subtract = false);

    /// <summary>Binds a duration for <see cref="ShiftByDuration" />, without overflow for any <see cref="TimeSpan" />.</summary>
    void AddDuration(DbCommand command, string parameter, TimeSpan duration);

    /// <summary>Binds a value typed for its column, so comparisons use the column's collation and indexes.</summary>
    void AddParameter(DbCommand command, string parameter, SqlColumnType type, object? value);

    /// <summary>Returns a keyset predicate: the row's <paramref name="columns" /> tuple sorts after the parameters.</summary>
    string KeysetAfter(IReadOnlyList<string> columns, IReadOnlyList<string> parameters);

    /// <summary>
    /// Returns a predicate that <paramref name="expression" /> equals one element of the list parameter
    /// <paramref name="parameter" />, bound with <see cref="AddListParameter{T}" />. An empty list matches nothing.
    /// </summary>
    /// <remarks>
    /// One parameter carries the whole list, so a statement's text, and its cached plan, does not change with the list's
    /// length, and no engine needs a table type created ahead of time.
    /// </remarks>
    string InList(string expression, string parameter, SqlColumnType elementType);

    /// <summary>Binds <paramref name="values" /> as the list parameter an <see cref="InList" /> predicate reads.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="elementType" /> is <see cref="SqlColumnKind.Binary" /> or <see cref="SqlColumnKind.Json" />.
    /// </exception>
    void AddListParameter<T>(
        DbCommand command,
        string parameter,
        SqlColumnType elementType,
        IReadOnlyCollection<T> values
    );

    /// <summary>
    /// Creates the list parameter an <see cref="InList" /> predicate reads, for a caller that builds its parameters
    /// before it has a command.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="elementType" /> is <see cref="SqlColumnKind.Binary" /> or <see cref="SqlColumnKind.Json" />.
    /// </exception>
    DbParameter CreateListParameter<T>(string parameter, SqlColumnType elementType, IReadOnlyCollection<T> values);

    /// <summary>
    /// Returns a predicate that the tuple of <paramref name="expressions" /> equals one row of the tuple list
    /// <paramref name="parameter" />, bound with <see cref="CreateTupleListParameters" />. An empty list matches nothing.
    /// </summary>
    /// <remarks>
    /// Matching each row as a whole keeps pairs together: two separate lists would also match one row's first value
    /// with another row's second. The parameter count does not grow with the list.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The counts differ, there are fewer than two expressions, or an element type is <see cref="SqlColumnKind.Binary" />
    /// or <see cref="SqlColumnKind.Json" />.
    /// </exception>
    string InTuples(IReadOnlyList<string> expressions, string parameter, IReadOnlyList<SqlColumnType> elementTypes);

    /// <summary>
    /// Creates the parameters an <see cref="InTuples" /> predicate reads: one per element on an engine that binds a
    /// list per column, or one for the whole list. Every row holds one non-null value per element type, in order.
    /// </summary>
    /// <exception cref="ArgumentException">A row has the wrong length or a null value, or an element type cannot be listed.</exception>
    IReadOnlyList<DbParameter> CreateTupleListParameters(
        string parameter,
        IReadOnlyList<SqlColumnType> elementTypes,
        IReadOnlyCollection<IReadOnlyList<object>> rows
    );

    /// <summary>Renders <see cref="SqlLockedRead" />.</summary>
    string Render(SqlLockedRead statement);

    /// <summary>Renders <see cref="SqlFencedTransition" />.</summary>
    string Render(SqlFencedTransition statement);

    /// <summary>Renders <see cref="SqlInsertIfAbsent" />.</summary>
    string Render(SqlInsertIfAbsent statement);

    /// <summary>Renders <see cref="SqlClaimNext" />.</summary>
    string Render(SqlClaimNext statement);

    /// <summary>Renders <see cref="SqlDeleteBatch" />.</summary>
    string Render(SqlDeleteBatch statement);

    /// <summary>Renders <see cref="SqlUpsert" />.</summary>
    string Render(SqlUpsert statement);

    /// <summary>Renders <see cref="SqlClockedStatement" />.</summary>
    string Render(SqlClockedStatement statement);

    /// <summary>Renders <see cref="SqlInsert" />.</summary>
    string Render(SqlInsert statement);

    /// <summary>Renders <see cref="SqlLockBatch" />.</summary>
    string Render(SqlLockBatch statement);

    /// <summary>Renders <see cref="SqlTransactionLock" />.</summary>
    string Render(SqlTransactionLock statement);

    /// <summary>Classifies a failure the engine's driver raised.</summary>
    SqlErrorKind Classify(Exception exception);
}
