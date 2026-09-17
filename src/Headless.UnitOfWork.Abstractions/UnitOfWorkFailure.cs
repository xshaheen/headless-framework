// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// The terminal failure recorded on a unit of work, handed to <see cref="IUnitOfWork.OnFailed" /> callbacks
/// and exposed as <see cref="IUnitOfWork.Failure" />.
/// </summary>
/// <param name="Reason">Why the unit failed.</param>
/// <param name="Exception">The originating fault, when one exists (a commit fault or the abandoning exception).</param>
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

    /// <summary>The resource's commit (or the completion drain's resource interaction) faulted.</summary>
    Faulted = 3,

    /// <summary>The unit was still active when its owning service scope disposed; the manager rolled it back.</summary>
    ScopeDisposed = 4,

    /// <summary>A child unit was abandoned, aborting this (root) unit.</summary>
    ChildAbandoned = 5,
}
