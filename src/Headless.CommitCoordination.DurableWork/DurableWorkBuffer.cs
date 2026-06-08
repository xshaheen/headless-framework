// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.CommitCoordination.DurableWork;

/// <summary>
/// Base class for durable work buffers that write rows inside the current relational transaction.
/// </summary>
/// <typeparam name="TRow">The durable row type.</typeparam>
[PublicAPI]
public abstract class DurableWorkBuffer<TRow>(
    ICommitCoordinator coordinator,
    DurableWorkProviderMismatchPolicy onProviderMismatch = DurableWorkProviderMismatchPolicy.Throw
) : ICommitWorkBuffer
{
    /// <summary>
    /// Enlists a row by writing it through the current relational capability.
    /// </summary>
    /// <param name="row">The row to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the durable write.</returns>
    public async ValueTask EnlistAsync(TRow row, CancellationToken cancellationToken)
    {
        if (!coordinator.TryGetCapability<IRelationalCommitContext>(out var relationalContext))
        {
            if (onProviderMismatch == DurableWorkProviderMismatchPolicy.Throw)
            {
                throw new InvalidOperationException(
                    $"Durable commit work requires {nameof(IRelationalCommitContext)}."
                );
            }

            await EnlistWithoutRelationalContextAsync(row, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteRowAsync(row, relationalContext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the row using the current relational context.
    /// </summary>
    /// <param name="row">The row to write.</param>
    /// <param name="relationalContext">The relational context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the write.</returns>
    protected abstract ValueTask WriteRowAsync(
        TRow row,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Handles opt-in non-relational fallback.
    /// </summary>
    /// <param name="row">The row to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the fallback.</returns>
    protected virtual ValueTask EnlistWithoutRelationalContextAsync(TRow row, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
