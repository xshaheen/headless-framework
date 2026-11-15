// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.UnitOfWork;

namespace Headless.Messaging.Transactions;

/// <summary>
/// The rows of one store batch on a unit, held between their publish and the batch's flush. See
/// <see cref="IOutboxStoreBatch" /> for the contract.
/// </summary>
/// <remarks>
/// Not thread-safe: a batch belongs to the one caller that publishes its run in sequence and then flushes it.
/// </remarks>
internal sealed class OutboxStoreBatch(MessagePublisher publisher, IUnitOfWork unitOfWork) : IOutboxStoreBatch
{
    private readonly List<OutboxMessageWriter.PendingRow> _pending = [];
    private OutboxMessageWriter? _writer;

    internal bool HasPending => _pending.Count > 0;

    /// <summary>The transaction the held rows are written in, or <see langword="null" /> when none is held.</summary>
    internal System.Data.Common.DbTransaction? PendingTransaction =>
        _pending.Count > 0 ? _pending[0].Transaction : null;

    public Task<PublishReceipt> PublishAsync<T>(
        T? contentObj,
        OutboxOptions? options,
        CancellationToken cancellationToken
    )
    {
        return publisher.PublishAsync(
            MessageLane.Bus,
            contentObj,
            options,
            unitOfWork,
            requireCoordination: true,
            this,
            cancellationToken
        );
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        OutboxMessageWriter.PendingRow[] rows = [.. _pending];
        _pending.Clear();
        await _writer!.FlushAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    internal void Hold(OutboxMessageWriter writer, OutboxMessageWriter.PendingRow row)
    {
        _writer = writer;
        _pending.Add(row);
    }

    public ValueTask DisposeAsync()
    {
        // Rows still held were never written: the run failed before its flush, and the unit rolls back.
        foreach (var row in _pending)
        {
            row.Trace.Activity?.Dispose();
        }

        _pending.Clear();

        return ValueTask.CompletedTask;
    }
}
