// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Microsoft.Data.SqlClient;

/// <summary>
/// Stands in for SqlClient's exception by its full type name, which is all the classifier reads: the real type has
/// no public constructor, and the unit-of-work core references no driver. Like the real one, it reports the first
/// error as <see cref="Number" /> and <see cref="Class" />, and every error through <see cref="Errors" />.
/// </summary>
internal sealed class SqlException(int number, byte severity = 16, Exception? inner = null, params int[] furtherNumbers)
    : DbException($"fake SqlClient failure {number}", inner)
{
    public int Number { get; } = number;

    public byte Class { get; } = severity;

    public IReadOnlyList<SqlError> Errors { get; } =
    [new SqlError(number, severity), .. furtherNumbers.Select(n => new SqlError(n))];
}

/// <summary>One error of a fake <see cref="SqlException" />; the classifier reads its number and severity.</summary>
internal sealed class SqlError(int number, byte severity = 16)
{
    public int Number { get; } = number;

    public byte Class { get; } = severity;
}
