// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>Optional storage capability for deleting scheduled messages before dispatch reservation.</summary>
/// <remarks>
/// The delete must atomically fence against reservation, terminal state, persisted retry state,
/// and the configured storage version. A claimed but unreserved row remains revocable.
/// Missing rows must never be resurrected by reservation or shutdown flush.
/// </remarks>
[PublicAPI]
public interface IMessageRevocationStorage
{
    /// <summary>Conditionally deletes a scheduled published row and returns the observed outcome.</summary>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    ValueTask<MessageRevocationResult> RevokeAsync(Guid storageId, CancellationToken cancellationToken = default);
}
