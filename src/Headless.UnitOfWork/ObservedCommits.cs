// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork.Internal;

namespace Headless.UnitOfWork;

/// <summary>
/// Reports the outcome of a commit that the caller of an observed unit (<c>Enlist</c>) ran itself, so the unit fails
/// with the same reason, and the caller throws the same exception, as an owned unit's <c>CompleteAsync</c>.
/// </summary>
internal static class ObservedCommits
{
    /// <summary>
    /// Fails <paramref name="unitOfWork"/> for a faulted commit and returns the exception to throw: a
    /// <see cref="UnitOfWorkInDoubtException"/> when the commit may have reached the database, otherwise
    /// <paramref name="commitFault"/>.
    /// </summary>
    /// <param name="unitOfWork">The observed unit enlisted over the transaction whose commit faulted.</param>
    /// <param name="commitFault">The exception the commit raised.</param>
    /// <param name="cancelledBeforeCommit">Whether the commit's token was already cancelled when the commit began.</param>
    public static ValueTask<Exception> FailAsync(
        IUnitOfWork unitOfWork,
        Exception commitFault,
        bool cancelledBeforeCommit
    )
    {
        if (unitOfWork is UnitOfWorkHandle handle)
        {
            return handle.FailObservedCommitAsync(commitFault, cancelledBeforeCommit);
        }

        // A unit from another factory cannot be failed from here; the caller still throws the classified exception.
        return ValueTask.FromResult<Exception>(
            InDoubtCommitFaults.IsInDoubt(commitFault, cancelledBeforeCommit)
                ? new UnitOfWorkInDoubtException(commitFault)
                : commitFault
        );
    }
}
