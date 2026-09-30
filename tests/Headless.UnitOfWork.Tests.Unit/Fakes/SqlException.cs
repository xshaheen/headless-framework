// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

#pragma warning disable IDE0130 // The namespace is the point: the classifier recognizes SqlClient by type name.
namespace Microsoft.Data.SqlClient;

/// <summary>
/// Stands in for SqlClient's exception by its full type name, which is all the classifier reads: the real type has
/// no public constructor, and the unit-of-work core references no driver. Like the real one, it reports the first
/// error as <see cref="Number" /> and every error through <see cref="Errors" />.
/// </summary>
internal sealed class SqlException(int number, params int[] furtherNumbers)
    : DbException($"fake SqlClient failure {number}")
{
    public int Number { get; } = number;

    public IReadOnlyList<SqlError> Errors { get; } =
    [new SqlError(number), .. furtherNumbers.Select(n => new SqlError(n))];
}

/// <summary>One error of a fake <see cref="SqlException" />; the classifier reads only <see cref="Number" />.</summary>
internal sealed class SqlError(int number)
{
    public int Number { get; } = number;
}
