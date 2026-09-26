// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>Provider-neutral delivery priority of a <see cref="PushNotificationRequest"/>.</summary>
/// <remarks>
/// APNs sends <see cref="High"/> as <c>apns-priority: 10</c> and <see cref="Normal"/> as <c>apns-priority: 5</c>;
/// Android sends them as the high and normal message priorities.
/// </remarks>
[PublicAPI]
public enum PushNotificationPriority
{
    /// <summary>Deliver when power considerations allow; the device may batch or delay the message.</summary>
    Normal = 0,

    /// <summary>Deliver immediately, waking the device if needed.</summary>
    High = 1,
}
