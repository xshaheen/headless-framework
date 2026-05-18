// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal static class MessagingKeys
{
    /// <summary>Keyed-DI service key for messaging's isolated <see cref="Headless.DistributedLocks.IDistributedLockProvider"/>.</summary>
    public const string LockProvider = "headless.messaging";
}
