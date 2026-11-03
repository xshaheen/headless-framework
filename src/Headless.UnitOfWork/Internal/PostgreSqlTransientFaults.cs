// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The PostgreSQL half of <see cref="RelationalTransientFaults" />: the SQLSTATEs a whole-transaction replay
/// cures. PostgreSQL's own guidance names exactly these two — <c>40001</c> is the SQL-standard serialization
/// failure, so any driver that reports SQLSTATE benefits, and <c>40P01</c> is PostgreSQL's deadlock. Npgsql's
/// <c>IsTransient</c> already covers both, plus its connection and capacity faults; this keeps replay working
/// for a driver, or a wrapped exception, that reports the SQLSTATE without the flag.
/// </summary>
internal static class PostgreSqlTransientFaults
{
    /// <summary>Whether a SQLSTATE reports a serialization failure or a deadlock, both cured by a replay.</summary>
    public static bool IsTransientSqlState(string? sqlState)
    {
        return sqlState is SqlErrorCodes.PostgreSql.SerializationFailure or SqlErrorCodes.PostgreSql.DeadlockDetected;
    }
}
