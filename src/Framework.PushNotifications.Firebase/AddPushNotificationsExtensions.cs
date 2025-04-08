// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FirebaseAdmin;
using Framework.Checks;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.PushNotifications.Firebase;

[PublicAPI]
public static class AddPushNotificationsExtensions
{
    public static IServiceCollection AddPushNotifications(this IServiceCollection services, FirebaseOptions options)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(options);

        if (FirebaseApp.DefaultInstance is null)
        {
            FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(options.Json) });
        }

        services.AddSingleton<IPushNotificationService, GoogleCloudMessagingPushNotificationService>();

        return services;
    }
}
