// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// The gap-free numbering surface behind <c>unit.Sequences</c>: every call increments the counter inside the unit's
/// own transaction, so the unit's rollback returns the number and its commit makes it permanent.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessSequences</c>. It holds no unit: the caller's handle arrives per call,
/// and the call refuses, before any command runs, a unit that is no longer active, carries no relational resource,
/// carries a completed transaction, or carries a transaction the configured provider cannot write through.
/// Application code reaches it through <c>unit.Sequences</c> rather than directly.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkSequences : IUnitOfWorkFeature
{
    /// <summary>Takes the gap-free counter's next value inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the increment.</param>
    /// <param name="name">The counter name; it must be registered as <see cref="SequenceMode.GapFree" />.</param>
    /// <param name="partition">Splits the counter; <see langword="null" /> or empty means no partition.</param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The value taken.</returns>
    /// <exception cref="ArgumentException">The name, partition, or current tenant id is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as gap-free, or the unit cannot host the write.
    /// </exception>
    ValueTask<long> NextAsync(
        IUnitOfWork unitOfWork,
        string name,
        string? partition = null,
        CancellationToken cancellationToken = default
    );
}
