// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase;

/// <summary>The Android delivery priority of an FCM message.</summary>
[PublicAPI]
public enum FcmAndroidPriority
{
    /// <summary>Delivered when the device is not in Doze; may be batched.</summary>
    Normal = 0,

    /// <summary>
    /// Delivered immediately, waking a dozing device. Android deprioritizes an app that sends high priority messages
    /// that show no notification.
    /// </summary>
    High = 1,
}
