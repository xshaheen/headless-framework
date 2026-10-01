// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.InteropServices;

namespace Headless.Sql;

#pragma warning disable CA1720 // Fix would make it worse: these members name SQL storage widths, and a name other than Int16/32/64 would hide which width each maps to.

/// <summary>
/// Everything a relational store needs to know about one database engine, so the store itself is written once:
/// identifier naming and quoting, parameters, time arithmetic, the statement shapes that lock, fence, claim, and
/// purge rows, and error classification. Schema objects are not its concern: each feature contributes its DDL to the
/// schema runner.
/// </summary>
/// <remarks>
/// <para>
/// Statement fragments a store hands to the dialect name columns unqualified and write the database clock as
/// <see cref="SqlDialectTokens.Now" />. Every rendered statement reads that clock only after the rows it decides on are
/// locked, so no decision is made on a clock read from before a lock wait.
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

    /// <summary>Classifies a failure the engine's driver raised.</summary>
    SqlErrorKind Classify(Exception exception);
}

/// <summary>Placeholders a store writes in statement fragments, replaced by each dialect.</summary>
[PublicAPI]
public static class SqlDialectTokens
{
    /// <summary>The database clock, read once per statement after the statement's row locks are held.</summary>
    public const string Now = "{now}";

    /// <summary>
    /// The row already stored under the key, in <see cref="SqlUpsert" />'s <c>Set</c> and <c>Guard</c>: write
    /// <c>{stored}.column</c> on the right of an assignment and in the guard. An assignment's target stays unqualified.
    /// </summary>
    public const string Stored = "{stored}";
}

/// <summary>A column's storage type, portable across dialects.</summary>
/// <param name="Kind">The kind of value.</param>
/// <param name="MaxLength">The maximum length of a text column; ignored for other kinds.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct SqlColumnType(SqlColumnKind Kind, int MaxLength = 0)
{
    /// <summary>
    /// Text compared and ordered ordinally (byte or code point), whatever the database's default collation, for key
    /// columns. Trailing-space padding is not suppressed by any collation; see <see cref="SqlPortable" />.
    /// </summary>
    public static SqlColumnType KeyText(int maxLength) => new(SqlColumnKind.KeyText, maxLength);

    /// <summary>Text with the database's default collation.</summary>
    public static SqlColumnType Text(int maxLength) => new(SqlColumnKind.Text, maxLength);

    public static SqlColumnType Int16 => new(SqlColumnKind.Int16);

    public static SqlColumnType Int32 => new(SqlColumnKind.Int32);

    public static SqlColumnType Int64 => new(SqlColumnKind.Int64);

    /// <summary>An instant with its offset, stored at <see cref="ISqlDialect.TimestampPrecision" />.</summary>
    public static SqlColumnType Timestamp => new(SqlColumnKind.Timestamp);

    public static SqlColumnType Binary => new(SqlColumnKind.Binary);

    /// <summary>A 16-byte identifier: <c>uuid</c> on PostgreSQL, <c>uniqueidentifier</c> on SQL Server.</summary>
    public static SqlColumnType Guid => new(SqlColumnKind.Guid);

    /// <summary>A boolean: <c>boolean</c> on PostgreSQL, <c>bit</c> on SQL Server.</summary>
    public static SqlColumnType Boolean => new(SqlColumnKind.Boolean);

    /// <summary>
    /// A JSON document, bound as text: <c>jsonb</c> on PostgreSQL, <c>nvarchar(max)</c> on SQL Server. Read it back as a
    /// string on both engines.
    /// </summary>
    public static SqlColumnType Json => new(SqlColumnKind.Json);
}

/// <summary>The kind of a portable column type.</summary>
[PublicAPI]
public enum SqlColumnKind
{
    KeyText = 0,
    Text = 1,
    Int16 = 2,
    Int32 = 3,
    Int64 = 4,
    Timestamp = 5,
    Binary = 6,

    /// <summary>A 16-byte identifier.</summary>
    Guid = 7,

    /// <summary>A boolean.</summary>
    Boolean = 8,

    /// <summary>A JSON document.</summary>
    Json = 9,
}
