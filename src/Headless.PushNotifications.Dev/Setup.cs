// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Dev;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications;

/// <summary>
/// Provides extension methods for registering the development push notification provider on <see cref="HeadlessPushNotificationsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupNoopPushNotifications
{
    extension(HeadlessPushNotificationsSetupBuilder setup)
    {
        /// <summary>
        /// Registers the development push notification provider as the default service.
        /// </summary>
        /// <returns>The builder instance for chaining.</returns>
        public HeadlessPushNotificationsSetupBuilder UseNoop()
        {
            setup.RegisterDefaultProvider(static services =>
                services.AddSingleton<IPushNotificationService, NoopPushNotificationService>()
            );

            return setup;
        }
    }
}

/// <summary>
/// Provides extension methods for registering the development push notification provider on <see cref="HeadlessPushNotificationsInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupNoopPushNotificationsNamed
{
    extension(HeadlessPushNotificationsInstanceBuilder instance)
    {
        /// <summary>
        /// Registers the development push notification provider for this named instance.
        /// </summary>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessPushNotificationsInstanceBuilder UseNoop()
        {
            var name = instance.Name;

            instance.RegisterProvider(services =>
                services.AddKeyedSingleton<IPushNotificationService, NoopPushNotificationService>(name)
            );

            return instance;
        }
    }
}
