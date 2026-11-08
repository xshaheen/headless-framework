// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Optional storage capability for releasing a locally completed or explicitly abandoned retry lease.
/// </summary>
/// <remarks>
/// Implementations must compare the complete <see cref="MessageLeaseIdentity"/> against durable state and end only
/// the row lease, leaving the message's own columns as they are. A stale identity, whose lease generation is no
/// longer the row's live one, must be a no-op. Callers must never invoke
/// this capability while the corresponding local handler is still executing.
/// </remarks>
[PublicAPI]
public interface IGracefulLeaseReleaseStorage
{
    /// <summary>Releases an exact published-message lease generation.</summary>
    ValueTask<bool> ReleasePublishedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases an exact received-message lease generation.</summary>
    ValueTask<bool> ReleaseReceivedLeaseAsync(
        MessageLeaseIdentity identity,
        CancellationToken cancellationToken = default
    );

    /// <summary>Releases exact published-message lease generations in a provider-bounded batch.</summary>
    async ValueTask<int> ReleasePublishedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        var released = 0;
        foreach (var identity in identities)
        {
            if (await ReleasePublishedLeaseAsync(identity, cancellationToken).ConfigureAwait(false))
            {
                released++;
            }
        }

        return released;
    }

    /// <summary>Releases exact received-message lease generations in a provider-bounded batch.</summary>
    async ValueTask<int> ReleaseReceivedLeasesAsync(
        IReadOnlyCollection<MessageLeaseIdentity> identities,
        CancellationToken cancellationToken = default
    )
    {
        var released = 0;
        foreach (var identity in identities)
        {
            if (await ReleaseReceivedLeaseAsync(identity, cancellationToken).ConfigureAwait(false))
            {
                released++;
            }
        }

        return released;
    }
}

/// <summary>
/// Identifies one store-authoritative retry lease generation.
/// </summary>
/// <param name="StorageId">The durable message row identifier.</param>
/// <param name="Generation">The row lease generation the claim operation granted.</param>
/// <param name="Lane">The persisted Bus or Queue lane returned by the claim operation.</param>
/// <param name="InboxAttemptFence">The complete inbox attempt fence when the lease belongs to an inbox row.</param>
[PublicAPI]
public readonly record struct MessageLeaseIdentity(
    Guid StorageId,
    long Generation,
    MessageLane Lane,
    InboxAttemptFence? InboxAttemptFence = null
);
