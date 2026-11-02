// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Tests.Fakes
{
    /// <summary>Stands in for Npgsql's <c>PostgresException</c>, which reports SQLSTATE through <see cref="DbException.SqlState"/>.</summary>
    internal sealed class SqlStateDbException(string sqlState) : DbException("provider error")
    {
        public override string SqlState { get; } = sqlState;
    }
}

namespace Microsoft.Data.SqlClient
{
    /// <summary>
    /// Stands in for SqlClient's <c>SqlException</c>; the classifier reads its public <c>Number</c> and <c>Errors</c>
    /// by name. Like the real one, <c>Number</c> is the first error's.
    /// </summary>
    internal sealed class SqlException(int number, params int[] furtherNumbers)
        : System.Data.Common.DbException("provider error")
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

namespace Microsoft.Data.Sqlite
{
    /// <summary>Stands in for <c>SqliteException</c>; the handler reads its public <c>SqliteExtendedErrorCode</c> by name.</summary>
    internal sealed class SqliteException(int extendedErrorCode) : System.Data.Common.DbException("provider error")
    {
        public int SqliteExtendedErrorCode { get; } = extendedErrorCode;
    }
}
