// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sql;

/// <summary>How a relational engine reported a failure, classified without referencing the driver's assembly.</summary>
[PublicAPI]
public enum SqlErrorKind
{
    /// <summary>The exception is not a provider error this classification covers.</summary>
    None = 0,

    /// <summary>A unique constraint or unique index rejected the write.</summary>
    UniqueViolation = 1,

    /// <summary>The engine chose the transaction as a deadlock victim.</summary>
    Deadlock = 2,

    /// <summary>A serialization or snapshot-conflict failure a fresh transaction can clear.</summary>
    SerializationConflict = 3,

    /// <summary>A concurrent creator already committed the object this DDL creates.</summary>
    DuplicateObject = 4,

    /// <summary>A lock wait timed out (lock_timeout, blocked session).</summary>
    LockTimeout = 5,

    /// <summary>
    /// The statement ran on a transaction an earlier error already ended or doomed (PostgreSQL <c>25P02</c>, SQL Server
    /// <c>3930</c>). Nothing more can run in it: the caller's unit of work is lost and must be rolled back, never
    /// retried in place.
    /// </summary>
    TransactionAborted = 6,
}
