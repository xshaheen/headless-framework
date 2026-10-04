// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Sequences;

/// <summary>
/// Defines the internal sequence service underlying <c>unit.Sequences</c> that commits counter increments within unit transactions.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessSequences</c>. Instances do not store transaction state.
/// Instead, the active <see cref="IUnitOfWork"/> is passed with each operation. Operations reject units that
/// are completed, missing relational resources, or using incompatible providers before issuing database commands.
/// Application code accesses sequence generation through <c>unit.Sequences</c>.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkSequences : IUnitOfWorkFeature
{
    /// <summary>Allocates the next value from a gap-free counter within the specified unit transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction scopes the counter increment.</param>
    /// <param name="name">The counter name. Must be registered as <see cref="SequenceMode.GapFree" />.</param>
    /// <param name="partition">An optional partition key. Pass <see langword="null" /> or an empty string to use no partition.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The allocated value.</returns>
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
}
