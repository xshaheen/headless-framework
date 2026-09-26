// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application's device-token store and the variables the push-notification guide's examples assume.

global using static PushNotificationsAmbient;
using Headless.PushNotifications;

public interface IDeviceTokenStore
{
    Task RemoveAsync(string deviceToken, CancellationToken cancellationToken);
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class PushNotificationsAmbient
{
    public static IPushNotificationService pushService => null!;

    public static IDeviceTokenStore tokenStore => null!;

    public static string deviceToken => null!;

    public static string json => null!;
}
