// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless;

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
        /// <summary>
        /// restrict_violation: a delete or update refused by a foreign key declared <c>ON DELETE RESTRICT</c> or
        /// <c>ON UPDATE RESTRICT</c>.
        /// </summary>
        public const string RestrictViolation = "23001";

        /// <summary>not_null_violation.</summary>
        public const string NotNullViolation = "23502";

        /// <summary>
        /// foreign_key_violation: a referenced row is missing, or a delete or update was refused by a foreign key
        /// declared <c>NO ACTION</c> (the default).
        /// </summary>
        public const string ForeignKeyViolation = "23503";

        /// <summary>check_violation.</summary>
        public const string CheckViolation = "23514";

        /// <summary>string_data_right_truncation: a value is longer than its column allows.</summary>
        public const string StringDataRightTruncation = "22001";

        /// <summary>numeric_value_out_of_range.</summary>
        public const string NumericValueOutOfRange = "22003";

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

        /// <summary>in_failed_sql_transaction: a statement ran after an error aborted the transaction.</summary>
        public const string InFailedSqlTransaction = "25P02";
    }

    /// <summary>SQL Server error numbers (<c>sys.messages</c>).</summary>
    public static class SqlServer
    {
        /// <summary>
        /// A constraint (foreign key or check constraint) prevented the statement. Distinct from the unique-key
        /// violations below, which raise 2601/2627 instead.
        /// </summary>
        public const int ConstraintViolation = 547;

        /// <summary>A NULL was written to a column that does not allow nulls.</summary>
        public const int CannotInsertNull = 515;

        /// <summary>Arithmetic overflow for a data type, such as 300 for <c>tinyint</c>.</summary>
        public const int ArithmeticOverflowForDataType = 220;

        /// <summary>Arithmetic overflow converting a value to a data type, such as too many digits for a <see langword="decimal" />.</summary>
        public const int ArithmeticOverflow = 8115;

        /// <summary>String or binary data would be truncated (before SQL Server 2019, or with trace flag 460 off).</summary>
        public const int StringOrBinaryDataTruncated = 8152;

        /// <summary>String or binary data would be truncated in a named column (SQL Server 2019 and later).</summary>
        public const int StringOrBinaryDataTruncatedInColumn = 2628;

        /// <summary>The transaction was chosen as the deadlock victim and rolled back by the engine.</summary>
        public const int DeadlockVictim = 1205;

        /// <summary>Cannot insert duplicate key row in an object with a unique index.</summary>
        public const int DuplicateKeyUniqueIndex = 2601;

        /// <summary>Violation of a unique constraint.</summary>
        public const int DuplicateKeyUniqueConstraint = 2627;

        /// <summary>Snapshot isolation transaction aborted due to an update conflict.</summary>
        public const int SnapshotUpdateConflict = 3960;

        /// <summary>Snapshot isolation transaction aborted because concurrent DDL changed an accessed object.</summary>
        public const int SnapshotMetadataConflict = 3961;

        /// <summary>The current transaction cannot be committed and cannot support operations that write to the log.</summary>
        public const int UncommittableTransaction = 3930;
    }
}
