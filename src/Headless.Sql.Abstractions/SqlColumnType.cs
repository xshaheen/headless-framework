// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.InteropServices;

namespace Headless.Sql;

#pragma warning disable CA1720 // Fix would make it worse: these members name SQL storage widths, and a name other than Int16/32/64 would hide which width each maps to.
/// <summary>A column's storage type, portable across dialects.</summary>
/// <param name="Kind">The kind of value.</param>
/// <param name="MaxLength">
/// The maximum length of a text column, or the exact length of a fixed-length binary column; ignored for other kinds.
/// </param>
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

    /// <summary>
    /// Text that looks up rows by a value compared with a column of <paramref name="maxLength" />: sized to the column,
    /// so the plan is reused and the comparison keeps the column's collation, unless <paramref name="value" /> is
    /// longer, in which case it binds unsized. A sized SQL Server parameter truncates an over-long value, which would
    /// then match the rows that store its prefix.
    /// </summary>
    public static SqlColumnType LookupText(int maxLength, string? value) =>
        Text(value?.Length > maxLength ? -1 : maxLength);

    /// <summary>
    /// The element type of a list of lookup values, sized as <see cref="LookupText(int, string)" /> sizes one value:
    /// unsized when any element is longer than the column.
    /// </summary>
    public static SqlColumnType LookupText(int maxLength, IEnumerable<string> values) =>
        Text(values.Any(value => value.Length > maxLength) ? -1 : maxLength);

    public static SqlColumnType Int16 => new(SqlColumnKind.Int16);

    public static SqlColumnType Int32 => new(SqlColumnKind.Int32);

    public static SqlColumnType Int64 => new(SqlColumnKind.Int64);

    /// <summary>An instant with its offset, stored at <see cref="ISqlDialect.TimestampPrecision" />.</summary>
    public static SqlColumnType Timestamp => new(SqlColumnKind.Timestamp);

    public static SqlColumnType Binary => new(SqlColumnKind.Binary);

    /// <summary>
    /// Bytes of exactly <paramref name="length" />: <c>binary(length)</c> on SQL Server, so a comparison with a
    /// fixed-length column keeps its index; <c>bytea</c> on PostgreSQL.
    /// </summary>
    public static SqlColumnType FixedBinary(int length) => new(SqlColumnKind.Binary, length);

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
