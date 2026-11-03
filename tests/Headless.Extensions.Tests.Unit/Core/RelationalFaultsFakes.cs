// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Microsoft.Data.SqlClient
{
    /// <summary>
    /// Stands in for SqlClient's exception by its full type name, which is all the classifier reads: the real type has
    /// no public constructor, and Headless.Extensions references no driver. Like the real one, it reports the first
    /// error as <see cref="Number" /> and every error through <see cref="Errors" />.
    /// </summary>
    internal sealed class SqlException(int number, params int[] furtherNumbers)
        : DbException($"fake SqlClient failure {number}")
    {
        public int Number { get; } = number;

        public IReadOnlyList<SqlError> Errors { get; } =
        [new SqlError(number), .. furtherNumbers.Select(n => new SqlError(n))];
    }

    /// <summary>One error of a fake <see cref="SqlException" />.</summary>
    internal sealed class SqlError(int number)
    {
        public int Number { get; } = number;
    }
}

namespace System.Data.SqlClient
{
    /// <summary>Stands in for the legacy SqlClient exception, exposing only its first error's number.</summary>
    internal sealed class SqlException(int number) : DbException($"fake legacy SqlClient failure {number}")
    {
        public int Number { get; } = number;
    }
}

namespace Microsoft.Data.Sqlite
{
    /// <summary>
    /// Stands in for Microsoft.Data.Sqlite's exception by its full type name; the classifier reads its extended
    /// result code.
    /// </summary>
    internal sealed class SqliteException(int errorCode, int extendedErrorCode)
        : DbException($"fake SQLite failure {errorCode}")
    {
        public int SqliteErrorCode { get; } = errorCode;

        public int SqliteExtendedErrorCode { get; } = extendedErrorCode;
    }
}
