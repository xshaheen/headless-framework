// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.PushNotifications;

/// <summary>
/// Provides extension methods for registering push notification services in <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class SetupPushNotificationsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless push notification services.
        /// </summary>
        /// <param name="configure">A delegate that configures the default service and named instances.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// More than one default provider is registered, a named instance is misconfigured, an instance name is duplicated,
        /// or push notification services were already registered.
        /// </exception>
        public IServiceCollection AddHeadlessPushNotifications(Action<HeadlessPushNotificationsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessPushNotificationsSetupBuilder(services);
            configure(setup);

            return _AddPushNotificationsProviderCore(services, setup);
        }
    }

    private static IServiceCollection _AddPushNotificationsProviderCore(
        IServiceCollection services,
        HeadlessPushNotificationsSetupBuilder setup
    )
    {
        if (setup.DefaultExtensions.Count > 1)
        {
            throw new InvalidOperationException(
                "Headless.PushNotifications allows at most one default push-notification provider. Multiple "
                    + "default providers were configured — register the additional services as named instances "
                    + "with `AddNamed`."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(PushNotificationsProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessPushNotifications was already called on this service collection. Configure all "
                    + "push-notification services (default and named) in a single AddHeadlessPushNotifications call."
            );
        }

        services.AddSingleton(new PushNotificationsProviderRegistration());

        var registeredNames = setup.InstanceNames.ToFrozenSet(StringComparer.Ordinal);
        services.TryAddSingleton<IPushNotificationServiceProvider>(
            provider => new KeyedServicePushNotificationServiceProvider(provider, registeredNames)
        );

        // Run default provider registration first, followed by each named instance.
        foreach (var action in setup.DefaultExtensions)
        {
            action(services);
        }

        foreach (var (_, action) in setup.NamedExtensions)
        {
            action(services);
        }

        return services;
    }

    private sealed record PushNotificationsProviderRegistration;
}
