// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications.Dev;

/// <summary>
/// Extension members for selecting the no-op (development/testing) push-notification provider as the default
/// (unkeyed) service on <see cref="HeadlessPushNotificationsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupNoopPushNotifications
{
    extension(HeadlessPushNotificationsSetupBuilder setup)
    {
        /// <summary>
        /// Selects the no-op provider, which sends nothing and always reports success. Intended for
        /// development and testing only.
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
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
/// Extension members for selecting the no-op (development/testing) push-notification provider for a named
/// instance on <see cref="HeadlessPushNotificationsInstanceBuilder"/>. The instance resolves as a keyed
/// <see cref="IPushNotificationService"/> or through <see cref="IPushNotificationServiceProvider"/>.
/// </summary>
[PublicAPI]
public static class SetupNoopPushNotificationsNamed
{
    extension(HeadlessPushNotificationsInstanceBuilder instance)
    {
        /// <summary>
        /// Uses the no-op provider for this named instance, sending nothing and always reporting success.
        /// Intended for development and testing only.
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
