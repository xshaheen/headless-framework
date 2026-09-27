// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;

namespace Tests;

/// <summary>Builders for push-notification request values used across push provider tests.</summary>
public static class PushNotificationRequests
{
    /// <summary>A minimal valid <see cref="PushNotificationRequest"/> every provider accepts.</summary>
    public static PushNotificationRequest Valid(
        string title = "Order shipped",
        string body = "Your order is on its way."
    )
    {
        return new PushNotificationRequest { Title = title, Body = body };
    }
}
