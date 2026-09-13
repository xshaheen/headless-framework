// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Revokes scheduled messages before their first dispatch reservation.</summary>
/// <remarks>
/// Revocation deletes the published row and retains no audit record. It is not tenant-scoped.
/// Applications must authorize access to stored handles. Use Jobs for keyed, replaceable,
/// tenant-scoped, or transactional deadlines. Revoke only after the publishing transaction commits.
/// </remarks>
[PublicAPI]
public interface IMessageRevoker
{
    /// <summary>Attempts to delete an accepted scheduled message by its durable storage handle.</summary>
    /// <param name="storageId">The durable handle returned from publish or enqueue.</param>
    /// <param name="cancellationToken">Cancels this storage request, not delivery of the accepted message.</param>
    /// <returns>The observed revocation outcome. Only <see cref="MessageRevocationResult.Revoked"/> proves prevention.</returns>
    /// <exception cref="OperationCanceledException">The request was cancelled.</exception>
    /// <exception cref="NotSupportedException">The configured storage provider does not support revocation.</exception>
    ValueTask<MessageRevocationResult> RevokeAsync(Guid storageId, CancellationToken cancellationToken = default);
}
