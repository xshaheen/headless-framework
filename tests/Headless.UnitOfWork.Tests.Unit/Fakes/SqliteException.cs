// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Microsoft.Data.Sqlite;

/// <summary>
/// Stands in for Microsoft.Data.Sqlite's exception by its full type name, which is all the classifier reads, since
/// the unit-of-work core references no driver. Like the real one, it leaves <see cref="DbException.IsTransient" />
/// false and reports the primary result code as <see cref="SqliteErrorCode" />.
/// </summary>
internal sealed class SqliteException(int errorCode, int extendedErrorCode)
    : DbException($"fake SQLite failure {errorCode}")
{
    public int SqliteErrorCode { get; } = errorCode;

    public int SqliteExtendedErrorCode { get; } = extendedErrorCode;
}
