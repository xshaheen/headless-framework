// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>Specifies the delivery priority of a push notification request.</summary>
/// <remarks>
/// APNs maps <see cref="High"/> to <c>apns-priority: 10</c> and <see cref="Normal"/> to <c>apns-priority: 5</c>.
/// Android maps them to high and normal message priorities.
/// </remarks>
[PublicAPI]
public enum PushNotificationPriority
{
    /// <summary>Delivers when power considerations allow. The device can batch or delay the message.</summary>
    Normal = 0,

    /// <summary>Delivers immediately, waking the device if needed.</summary>
    High = 1,
}
