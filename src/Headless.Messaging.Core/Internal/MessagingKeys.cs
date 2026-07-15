// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal static class MessagingKeys
{
    /// <summary>Keyed-DI service key for messaging's isolated <see cref="Headless.DistributedLocks.IDistributedLock"/>.</summary>
    public const string LockProvider = "headless.messaging";

    /// <summary>
    /// Builds the canonical lock resource name for the published-retry pickup loop.
    /// The <paramref name="version"/> is the per-deployment isolation key from
    /// <see cref="Configuration.MessagingOptions.Version"/> — two services sharing a single
    /// lock store MUST use distinct versions to avoid colliding on the same resource.
    /// </summary>
    public static string PublishRetryResource(string version)
    {
        return $"messaging.publish-retry-{version}";
    }

    /// <summary>
    /// Builds the canonical lock resource name for the received-retry pickup loop.
    /// The <paramref name="version"/> is the per-deployment isolation key from
    /// <see cref="Configuration.MessagingOptions.Version"/> — two services sharing a single
    /// lock store MUST use distinct versions to avoid colliding on the same resource.
    /// </summary>
    public static string ReceiveRetryResource(string version)
    {
        return $"messaging.receive-retry-{version}";
    }
}
