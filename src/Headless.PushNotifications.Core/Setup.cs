// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.PushNotifications;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering Headless push-notification services.
/// </summary>
[PublicAPI]
public static class SetupPushNotificationsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless push-notification services from a single setup builder. Provider packages
        /// contribute the default (unkeyed) service through <c>Use*</c> extensions on
        /// <see cref="HeadlessPushNotificationsSetupBuilder"/> (for example <c>UseFirebase</c>, <c>UseNoop</c>)
        /// and named services through <c>setup.AddNamed(name, i =&gt; i.Use*(…))</c>. A default service is
        /// optional (at most one); named services are optional and unbounded. Contributions are queued and not
        /// run until the setup gates pass, so a setup that fails a gate leaves the service collection unchanged
        /// (provider <c>Use*</c> members also validate their inputs synchronously before queuing).
        /// </summary>
        /// <param name="configure">Delegate that selects the default service and any named services.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the delegate registers more than one default provider, configures a named instance with
        /// zero or multiple providers, reuses a name, or when <c>AddHeadlessPushNotifications</c> has already
        /// been called on the same <see cref="IServiceCollection"/>.
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

        // Default first, then each named instance — nothing touched `services` until the gates above passed.
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
