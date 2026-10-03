// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>Whether a broadcast channel keeps a message for devices that are offline when it is sent.</summary>
[PublicAPI]
public enum ApnsChannelStoragePolicy
{
    /// <summary>
    /// APNs stores nothing and delivers each message once. Allows a higher publishing budget, for frequent updates
    /// such as live scores. A broadcast on such a channel must not set a nonzero expiration.
    /// </summary>
    NoMessageStored = 0,

    /// <summary>
    /// APNs keeps the most recent message for up to 8 hours for devices that are offline, for infrequent updates such
    /// as flight status.
    /// </summary>
    MostRecentMessageStored = 1,
}
