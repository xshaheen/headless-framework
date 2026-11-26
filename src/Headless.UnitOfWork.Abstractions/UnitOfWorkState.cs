// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Describes the lifecycle state of a unit of work.
/// </summary>
/// <remarks>
/// Completion moves from <see cref="Active" /> through <see cref="Completing" /> to
/// <see cref="Completed" /> or <see cref="Failed" />. Rollback and abandonment move from
/// <see cref="Active" /> directly to <see cref="Failed" />. Only <see cref="Active" /> accepts registrations.
/// </remarks>
[PublicAPI]
public enum UnitOfWorkState
{
    /// <summary>The unit is open, owns its resource (if any), and accepts registrations.</summary>
    Active = 0,

    /// <summary>
    /// The unit completed: owned mode committed the resource's transaction, observed mode observed the
    /// caller's commit. The completion drain may still be in flight when this state is first observed.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// The unit failed after a rollback, abandonment, or commit fault. See <see cref="IUnitOfWork.Failure" /> for the reason.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// <see cref="IUnitOfWork.CompleteAsync" /> owns the unit and is settling the outcome. The unit refuses new work.
    /// </summary>
    Completing = 3,
}
