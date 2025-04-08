// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Framework.PushNotifications.Dev;

[PublicAPI]
public static class AddPushNotificationsExtensions
{
    public static IServiceCollection AddNoopPushNotification(this IServiceCollection services)
    {
        services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();

        return services;
    }
}
