// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork;

/// <summary>
/// Describes the lifecycle state of a unit of work.
/// </summary>
/// <remarks>
/// Transitions are one-way and atomic: <see cref="Active" /> → <see cref="Completed" /> or
/// <see cref="Active" /> → <see cref="Failed" />. Registrations are accepted only while
/// <see cref="Active" />; a registration after the terminal state throws
/// <see cref="InvalidOperationException" />.
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
    /// The unit failed: an explicit rollback, an abandon, a commit fault, a child abandon aborting the root,
    /// or the scope disposing while the unit was active. See <see cref="IUnitOfWork.Failure" /> for the reason.
    /// </summary>
    Failed = 2,
}
