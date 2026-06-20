// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications.Dev;

/// <summary>Registration helper for the no-op (development/testing) push-notification provider.</summary>
[PublicAPI]
public static class SetupNoopPushNotification
{
    /// <summary>
    /// Registers <see cref="NoopPushNotificationService"/> as the singleton
    /// <see cref="IPushNotificationService"/>. Intended for development and testing only.
    /// </summary>
    public static IServiceCollection AddNoopPushNotification(this IServiceCollection services)
    {
        services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();

        return services;
    }
}
