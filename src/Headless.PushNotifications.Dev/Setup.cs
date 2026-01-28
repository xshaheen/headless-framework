// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications.Dev;

[PublicAPI]
public static class NoopPushNotificationSetup
{
    public static IServiceCollection AddNoopPushNotification(this IServiceCollection services)
    {
        services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();

        return services;
    }
}
