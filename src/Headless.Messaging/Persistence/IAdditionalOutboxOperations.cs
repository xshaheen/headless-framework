// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Audited re-send and delete of a published row that an additional outbox holds. The outbox's database holds no
/// operator ledger, so the change runs on the outbox's storage and its receipt is written to the primary storage
/// afterwards, tagged with the outbox's name.
/// </summary>
/// <remarks>
/// The two writes are on different databases and are not atomic: a receipt that fails to write leaves the row
/// changed, and the caller reports that beside the outcome. Replay under a used operation id is decided by the
/// primary's ledger when the receipt is written, after the row already changed.
/// </remarks>
internal interface IAdditionalOutboxOperations
{
    /// <summary>
    /// On an additional outbox's storage: re-sends or deletes the row under its lock, with the same eligibility as a
    /// ledger-holding store, and writes no receipt.
    /// </summary>
    /// <exception cref="InvalidOperationException">The storage holds the operator ledger, so it records its own receipts.</exception>
    ValueTask<PublishedOperationResult> ApplyPublishedWithoutReceiptAsync(
        MessagingOperationType operationType,
        PublishedOperationRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// On the primary storage: records the receipt and audit row of a change an additional outbox applied. A receipt
    /// already stored under the operation id replays when it matches, otherwise the call is a conflict.
    /// </summary>
    /// <exception cref="NotSupportedException">The storage holds no operator ledger.</exception>
    ValueTask<PublishedOperationResult> RecordOutboxReceiptAsync(
        string outbox,
        PublishedOperationResult applied,
        CancellationToken cancellationToken
    );
}
