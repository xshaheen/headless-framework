// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork;

/// <summary>
/// A relational <see cref="IUnitOfWorkResource" />: exposes the live connection and transaction so
/// participants (outbox writers, job writers) can place durable rows inside the caller's transaction.
/// </summary>
/// <remarks>
/// Both properties are non-null while the unit is active. Participants snapshot the handle and validate
/// transaction identity before writing, exactly as they validated the relational commit context before.
/// </remarks>
[PublicAPI]
public interface IRelationalUnitOfWorkResource : IUnitOfWorkResource
{
    /// <summary>Gets the active database connection. Non-null while the unit is active.</summary>
    DbConnection Connection { get; }

    /// <summary>Gets the active database transaction. Non-null while the unit is active.</summary>
    DbTransaction Transaction { get; }
}
