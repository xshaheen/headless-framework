// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// The terminal failure recorded on a unit of work, handed to <see cref="IUnitOfWork.OnFailed" /> callbacks
/// and exposed as <see cref="IUnitOfWork.Failure" />.
/// </summary>
/// <param name="Reason">Why the unit failed.</param>
/// <param name="Exception">
/// The originating fault, when one exists: the commit fault (for <see cref="UnitOfWorkFailureReason.InDoubt" />, the
/// <see cref="UnitOfWorkInDoubtException" /> the commit threw) or the abandoning exception.
/// </param>
[PublicAPI]
public sealed record UnitOfWorkFailure(UnitOfWorkFailureReason Reason, Exception? Exception = null);

/// <summary>
/// Why a unit of work reached its failed terminal state.
/// </summary>
[PublicAPI]
public enum UnitOfWorkFailureReason
{
    /// <summary>
    /// The default sentinel. A failure is never recorded with this reason; it exists so an unset value is
    /// distinguishable from a real one.
    /// </summary>
    Unspecified = 0,

    /// <summary>An explicit <see cref="IUnitOfWork.RollbackAsync" /> (owned mode) or the owner reporting its own rollback (observed mode).</summary>
    RolledBack = 1,

    /// <summary>The unit was disposed without <see cref="IUnitOfWork.CompleteAsync" /> or <see cref="IUnitOfWork.RollbackAsync" />.</summary>
    Abandoned = 2,

    /// <summary>
    /// The resource's commit faulted before it could have reached the database, or the database answered it with an
    /// error: the transaction did not commit.
    /// </summary>
    Faulted = 3,

    /// <summary>
    /// The commit request may have reached the database, but the connection failed (or timed out) before an answer
    /// came back, so whether the transaction committed is unknown. <see cref="IUnitOfWork.CompleteAsync" /> throws
    /// <see cref="UnitOfWorkInDoubtException" />; check a durable idempotency key before retrying the business
    /// operation.
    /// </summary>
    InDoubt = 4,
}
