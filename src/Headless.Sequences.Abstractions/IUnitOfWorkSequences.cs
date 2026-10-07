// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Defines the gap-free numbering surface behind <c>unit.Sequences</c>: every call increments the counter
/// inside the unit's own transaction, so the unit's rollback returns the number and its commit makes it
/// permanent.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessSequences</c>. It holds no unit: the caller's handle arrives per
/// call, and the call refuses, before any command runs, a unit that is no longer active, carries no relational
/// resource, carries a completed transaction, or carries a transaction the configured provider cannot write
/// through. Application code reaches it through <c>unit.Sequences</c> rather than directly.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkSequences : IUnitOfWorkFeature
{
    /// <summary>Takes the gap-free counter's next value inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the increment.</param>
    /// <param name="name">The counter name; it must be registered as <see cref="SequenceMode.GapFree" />.</param>
    /// <param name="partition">Splits the counter; <see langword="null" /> or empty means no partition.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The value taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork"/> is <see langword="null"/>, or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" /> is empty, whitespace, or invalid, or <paramref name="partition" /> or the tenant identifier contains invalid characters or exceeds maximum length.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as gap-free, or the unit cannot host the write.
    /// </exception>
    ValueTask<long> NextAsync(
        IUnitOfWork unitOfWork,
        string name,
        string? partition = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Takes the gap-free counter's next value as a document number inside <paramref name="unitOfWork" />'s
    /// transaction, in the partition its policy's reset chooses for today and formatted with its template.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the counter's row lock until it ends.</param>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.GapFree" />.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The document number.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name" /> or the tenant identifier is invalid.</exception>
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as gap-free, or the unit cannot host the write.
    /// </exception>
    ValueTask<SequenceNumber> NextNumberAsync(
        IUnitOfWork unitOfWork,
        string name,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Moves the reported counter forward to <paramref name="value" /> inside <paramref name="unitOfWork" />'s
    /// transaction, unless it already accepted that value or a higher one.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the counter's row lock until it ends.</param>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.Reported" />.</param>
    /// <param name="value">The value the caller reported.</param>
    /// <param name="partition">
    /// Splits the counter, for example by device. <see langword="null" /> or empty means no partition.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether the value was the next one, skipped ahead, or stale, and the highest value accepted before.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name" />, <paramref name="partition" />, or the tenant identifier is invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The counter is not registered as reported, or the unit cannot host the write.
    /// </exception>
    ValueTask<SequenceAdvance> AdvanceToAsync(
        IUnitOfWork unitOfWork,
        string name,
        long value,
        string? partition = null,
        CancellationToken cancellationToken = default
    );
}
