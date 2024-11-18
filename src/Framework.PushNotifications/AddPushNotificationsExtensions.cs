// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin;
using Framework.PushNotifications.Dev;
using Framework.PushNotifications.Gcm;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.PushNotifications;

[PublicAPI]
public static class AddPushNotificationsExtensions
{
    public static IServiceCollection AddPushNotifications(this IServiceCollection services, FirebaseOptions options)
    {
        _LoadFirebase(options.Json);
        services.AddSingleton<IPushNotificationService, GoogleCloudMessagingPushNotificationService>();

        return services;
    }

    public static IServiceCollection AddNoopPushNotification(this IServiceCollection services)
    {
        services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();

        return services;
    }

    private static void _LoadFirebase(string json)
    {
        if (FirebaseApp.DefaultInstance is null)
        {
            FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(json) });
        }
    }
}
