// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FirebaseAdmin;
using Framework.Integrations.PushNotifications.Dev;
using Framework.Integrations.PushNotifications.Gcm;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Integrations.PushNotifications;

[PublicAPI]
public static class AddPushNotificationsExtensions
{
    public static void AddPushNotifications(this IServiceCollection services, FirebaseSettings settings)
    {
        _LoadFirebase(settings.Json);
        services.AddSingleton<IPushNotificationService, GoogleCloudMessagingPushNotificationService>();
    }

    public static void AddNoopPushNotification(this IServiceCollection services)
    {
        services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();
    }

    private static void _LoadFirebase(string json)
    {
        if (FirebaseApp.DefaultInstance is null)
        {
            FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(json) });
        }
    }
}
