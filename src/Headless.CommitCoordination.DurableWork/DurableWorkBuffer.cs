// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.DurableWork;

/// <summary>
/// Base class for durable work buffers that write rows atomically inside the current relational transaction,
/// providing at-least-once delivery guarantees via the outbox pattern.
/// </summary>
/// <remarks>
/// Subclasses implement <see cref="WriteRowAsync" /> to insert a durable row (e.g. an outbox message) using
/// the live connection and transaction supplied by <see cref="IRelationalCommitContext" />. This ensures the
/// row is committed atomically with the domain data — it either commits with the transaction or is rolled
/// back with it.
/// <para>
/// When no <see cref="IRelationalCommitContext" /> is attached to the current coordinator (i.e. the caller is
/// using a non-relational or in-memory signal source), behavior is governed by
/// <paramref name="onProviderMismatch" />:
/// <list type="bullet">
///   <item><description><see cref="DurableWorkProviderMismatchPolicy.Throw" /> — throws immediately (default, fail-closed).</description></item>
///   <item><description><see cref="DurableWorkProviderMismatchPolicy.Warn" /> — logs a warning and delegates to
///   <see cref="EnlistWithoutRelationalContextAsync" />. The base implementation of that method also throws;
///   override it to supply a genuinely durable fallback.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <typeparam name="TRow">The type of the durable row written inside the transaction.</typeparam>
[PublicAPI]
public abstract partial class DurableWorkBuffer<TRow>(
    ICommitCoordinator coordinator,
    DurableWorkProviderMismatchPolicy onProviderMismatch = DurableWorkProviderMismatchPolicy.Throw,
    ILogger? logger = null
) : ICommitWorkBuffer
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>
    /// Writes <paramref name="row" /> inside the current relational transaction using the
    /// <see cref="IRelationalCommitContext" /> attached to the coordinator.
    /// </summary>
    /// <remarks>
    /// The row is written immediately (not deferred to drain time), so it commits or rolls back with the
    /// enclosing transaction. Call this during the transaction, not from an <see cref="ICommitCoordinator.OnCommit" />
    /// callback.
    /// </remarks>
    /// <param name="row">The row to write inside the transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="IRelationalCommitContext" /> is available and the configured provider-mismatch policy is
    /// <see cref="DurableWorkProviderMismatchPolicy.Throw" />, or the
    /// <see cref="EnlistWithoutRelationalContextAsync" /> override is not provided when
    /// <see cref="DurableWorkProviderMismatchPolicy.Warn" /> is used.
    /// </exception>
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

            if (onProviderMismatch == DurableWorkProviderMismatchPolicy.Warn)
            {
                LogProviderMismatch(_logger, typeof(TRow).Name);
            }

            await EnlistWithoutRelationalContextAsync(row, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteRowAsync(row, relationalContext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the row atomically inside the open relational transaction.
    /// </summary>
    /// <param name="row">The row to write.</param>
    /// <param name="relationalContext">
    /// The relational context exposing the live <c>DbConnection</c> and <c>DbTransaction</c> to use for the
    /// write.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    protected abstract ValueTask WriteRowAsync(
        TRow row,
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Called when no <see cref="IRelationalCommitContext" /> is available and
    /// <see cref="DurableWorkProviderMismatchPolicy.Warn" /> is in effect. Override to supply a genuinely
    /// durable non-relational write (e.g. an HTTP call to an external store or a direct database insert that
    /// does not need transactional enrollment).
    /// </summary>
    /// <remarks>
    /// The base implementation <b>always throws</b> (<see cref="InvalidOperationException" />). This is
    /// intentional: a consumer that opts into <see cref="DurableWorkProviderMismatchPolicy.Warn" /> without
    /// providing a real fallback would silently drop the row otherwise, voiding the at-least-once guarantee.
    /// </remarks>
    /// <param name="row">The row to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the fallback write has completed.</returns>
    /// <exception cref="InvalidOperationException">
    /// Always thrown by the base implementation. Derived classes that override this method should not call
    /// <c>base.EnlistWithoutRelationalContextAsync</c>.
    /// </exception>
    protected virtual ValueTask EnlistWithoutRelationalContextAsync(TRow row, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            $"Durable commit work for '{typeof(TRow).Name}' was enlisted without an {nameof(IRelationalCommitContext)} "
                + $"and this buffer does not override {nameof(EnlistWithoutRelationalContextAsync)} to provide a durable "
                + $"fallback. Provide a relational context, override the fallback, or use "
                + $"{nameof(DurableWorkProviderMismatchPolicy)}.{nameof(DurableWorkProviderMismatchPolicy.Throw)}."
        );
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Durable commit work row {RowType} was enlisted without a relational commit context."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogProviderMismatch(ILogger logger, string rowType);
}
