// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql.Sqlite;

/// <summary>The SQLite result codes the dialects classify, from <c>sqlite3.h</c>.</summary>
internal static class SqliteErrors
{
    public const int Error = 1;
    public const int Busy = 5;
    public const int Locked = 6;
    public const int Constraint = 19;
    public const int BusySnapshot = Busy | (2 << 8);
    public const int ConstraintPrimaryKey = Constraint | (6 << 8);
    public const int ConstraintUnique = Constraint | (8 << 8);

    /// <summary>
    /// Returns whether <paramref name="exception" /> is <c>Microsoft.Data.Sqlite</c> refusing a statement because its
    /// transaction already ended. SQLite rolls a whole transaction back on some failures (a full disk, an I/O error, an
    /// <c>OR ROLLBACK</c> conflict) and the driver then refuses every later statement on it before SQLite sees one, so
    /// nothing runs in autocommit. The driver raises a plain <see cref="InvalidOperationException" /> for it, told
    /// apart only by its message.
    /// </summary>
    public static bool IsCompletedTransaction(Exception exception)
    {
        return exception is InvalidOperationException { Source: "Microsoft.Data.Sqlite" } invalid
            && invalid.Message.StartsWith("This SqliteTransaction has completed", StringComparison.Ordinal);
    }
}
