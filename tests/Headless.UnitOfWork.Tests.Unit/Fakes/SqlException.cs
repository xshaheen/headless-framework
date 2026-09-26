// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

#pragma warning disable IDE0130 // The namespace is the point: the classifier recognizes SqlClient by type name.
namespace Microsoft.Data.SqlClient;

/// <summary>
/// Stands in for SqlClient's exception by its full type name, which is all the classifier reads: the real type has
/// no public constructor, and the unit-of-work core references no driver.
/// </summary>
internal sealed class SqlException(int number, byte severity = 16, Exception? inner = null)
    : DbException($"fake SqlClient failure {number}", inner)
{
    public int Number { get; } = number;

    public byte Class { get; } = severity;
}
