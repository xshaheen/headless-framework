// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.PushNotifications.Dev;

/// <summary>
/// Registers the no-op (development/testing) provider on a <see cref="HeadlessPushNotificationsSetupBuilder"/>.
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
        public HeadlessPushNotificationsSetupBuilder UseNoop()
        {
            setup.RegisterExtension(new NoopProviderOptionsExtension());

            return setup;
        }
    }

    private sealed class NoopProviderOptionsExtension : IPushNotificationsProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<IPushNotificationService, NoopPushNotificationService>();
        }
    }
}
