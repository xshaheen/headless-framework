// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Constants;

/// <summary>
/// Standard error codes for the supported SQL providers, used to classify a caught database exception (constraint
/// violation, deadlock, serialization/snapshot conflict, lock timeout) without depending on the provider's driver
/// assembly.
/// </summary>
[PublicAPI]
public static class SqlErrorCodes
{
    /// <summary>PostgreSQL <c>SQLSTATE</c> codes (https://www.postgresql.org/docs/current/errcodes-appendix.html).</summary>
    public static class PostgreSql
    {
        /// <summary>foreign_key_violation.</summary>
        public const string ForeignKeyViolation = "23503";

        /// <summary>unique_violation.</summary>
        public const string UniqueViolation = "23505";

        /// <summary>serialization_failure.</summary>
        public const string SerializationFailure = "40001";

        /// <summary>deadlock_detected.</summary>
        public const string DeadlockDetected = "40P01";

        /// <summary>duplicate_schema.</summary>
        public const string DuplicateSchema = "42P06";

        /// <summary>duplicate_table.</summary>
        public const string DuplicateTable = "42P07";

        /// <summary>duplicate_object (raised for a concurrently created index, constraint, or other named object).</summary>
        public const string DuplicateObject = "42710";

        /// <summary>lock_timeout.</summary>
        public const string LockTimeout = "55P03";
    }

    /// <summary>SQL Server error numbers (<c>sys.messages</c>).</summary>
    public static class SqlServer
    {
        /// <summary>
        /// A constraint (foreign key or check constraint) prevented the statement. Distinct from the unique-key
        /// violations below, which raise 2601/2627 instead.
        /// </summary>
        public const int ConstraintViolation = 547;

        /// <summary>The transaction was chosen as the deadlock victim and rolled back by the engine.</summary>
        public const int DeadlockVictim = 1205;

        /// <summary>Cannot insert duplicate key row in an object with a unique index.</summary>
        public const int DuplicateKeyUniqueIndex = 2601;

        /// <summary>Violation of a unique constraint.</summary>
        public const int DuplicateKeyUniqueConstraint = 2627;

        /// <summary>Snapshot isolation transaction aborted due to an update conflict.</summary>
        public const int SnapshotUpdateConflict = 3960;
    }
}
