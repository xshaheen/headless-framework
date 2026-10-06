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
    /// <summary>Stands in for SqlClient's <c>SqlException</c>; the handler reads its public <c>Number</c> by name.</summary>
    internal sealed class SqlException(int number) : System.Data.Common.DbException("provider error")
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
