// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>
/// Implemented by a store that may refuse to draw a lease's generation inside a caller's unit of work, because a
/// generation drawn in a transaction that then rolls back could be issued again.
/// </summary>
internal interface ILeaseEnlistedGrantGuard
{
    /// <summary>Throws when a grant cannot run inside <paramref name="unitOfWork" />.</summary>
    /// <exception cref="NotSupportedException">The store cannot draw a generation inside a caller's transaction.</exception>
    void ValidateEnlistedGrant(IUnitOfWork unitOfWork);
}
