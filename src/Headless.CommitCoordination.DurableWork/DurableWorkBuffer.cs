// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.DurableWork;

/// <summary>
/// Base class for durable work buffers that write rows inside the current relational transaction.
/// </summary>
/// <typeparam name="TRow">The durable row type.</typeparam>
[PublicAPI]
public abstract partial class DurableWorkBuffer<TRow>(
    ICommitCoordinator coordinator,
    DurableWorkProviderMismatchPolicy onProviderMismatch = DurableWorkProviderMismatchPolicy.Throw,
    ILogger? logger = null
) : ICommitWorkBuffer
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

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
    /// Handles opt-in non-relational fallback. The base implementation fails closed: a non-<see cref="DurableWorkProviderMismatchPolicy.Throw" />
    /// policy is only safe when a derived buffer overrides this with a genuinely durable write. The base must not
    /// silently drop the row — that would void the "no work lost" floor for a consumer that opted into
    /// <see cref="DurableWorkProviderMismatchPolicy.Warn" /> without supplying a real fallback.
    /// </summary>
    /// <param name="row">The row to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the fallback.</returns>
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
    private static partial void LogProviderMismatch(ILogger logger, string rowType);
}
