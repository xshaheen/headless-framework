// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Microsoft.Data.SqlClient
{
    /// <summary>
    /// Stands in for SqlClient's exception by its full type name, which is all the classifier reads: the real type has
    /// no public constructor, and Headless.Extensions references no driver. Like the real one, it reports the first
    /// error as <see cref="Number" /> and every error through <see cref="Errors" />.
    /// </summary>
    internal sealed class SqlException(params SqlError[] errors) : DbException(errors[0].Message)
    {
        public SqlException(int number, params int[] furtherNumbers)
            : this([new SqlError(number), .. furtherNumbers.Select(n => new SqlError(n))]) { }

        public int Number { get; } = errors[0].Number;

        public IReadOnlyList<SqlError> Errors { get; } = errors;
    }

    /// <summary>One error of a fake <see cref="SqlException" />.</summary>
    internal sealed class SqlError(int number, string? message = null)
    {
        public int Number { get; } = number;

        public string Message { get; } = message ?? $"fake SqlClient failure {number}";
    }
}

namespace System.Data.SqlClient
{
    /// <summary>Stands in for the legacy SqlClient exception, exposing only its first error's number.</summary>
    internal sealed class SqlException(int number, string? message = null)
        : DbException(message ?? $"fake legacy SqlClient failure {number}")
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

namespace Npgsql
{
    /// <summary>
    /// Stands in for Npgsql's exception: it reports SQLSTATE through <see cref="DbException.SqlState" /> and the
    /// violated constraint in its own <see cref="ConstraintName" /> field.
    /// </summary>
    internal sealed class PostgresException(string sqlState, string? constraintName)
        : DbException($"fake PostgreSQL failure {sqlState}")
    {
        public override string SqlState { get; } = sqlState;

        public string? ConstraintName { get; } = constraintName;
    }
}
