// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The Live Activity lifecycle event a Live Activity push carries, sent as the <c>"event"</c> key.
/// </summary>
[PublicAPI]
public enum ApnsLiveActivityEvent
{
    /// <summary>Starts a new Live Activity. Send it to the app's push-to-start token.</summary>
    Start = 0,

    /// <summary>Updates a running Live Activity. Send it to the activity's push token.</summary>
    Update = 1,

    /// <summary>Ends a running Live Activity. Send it to the activity's push token.</summary>
    End = 2,
}
