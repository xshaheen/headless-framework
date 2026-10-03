// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>
/// Optional storage capability for releasing a locally completed or explicitly abandoned retry lease.
/// </summary>
/// <remarks>
/// Implementations must compare the complete <see cref="MessageLeaseIdentity"/> against durable state and
/// clear only <c>Owner</c> and <c>LockedUntil</c>. A stale identity must be a no-op. Callers must never invoke
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
