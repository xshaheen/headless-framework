// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
using Headless.PushNotifications.Dev;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications;

/// <summary>
/// Extension members for selecting the development (capturing) or no-op push notification provider as the default
/// (unkeyed) service on <see cref="HeadlessPushNotificationsSetupBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupDevPushNotifications
{
    extension(HeadlessPushNotificationsSetupBuilder setup)
    {
        /// <summary>
        /// Selects the development provider with default options. It captures each notification in
        /// <see cref="IPushNotificationCaptureStore"/> instead of sending it. Intended for development only.
        /// </summary>
        /// <returns>The same builder for chaining.</returns>
        public HeadlessPushNotificationsSetupBuilder UseDevelopment()
        {
            return setup.UseDevelopment(static _ => { });
        }

        /// <summary>
        /// Selects the development provider, binding <see cref="DevPushNotificationOptions"/> from the supplied
        /// configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="DevPushNotificationOptions"/> properties.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseDevelopment(IConfiguration config)
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                DevPushNotificationRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevPushNotificationOptions, DevPushNotificationOptionsValidator>(config, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Selects the development provider, configuring <see cref="DevPushNotificationOptions"/> through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that populates the options.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseDevelopment(Action<DevPushNotificationOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                DevPushNotificationRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevPushNotificationOptions, DevPushNotificationOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Selects the development provider, configuring <see cref="DevPushNotificationOptions"/> through a delegate
        /// that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">A delegate that populates the options using dependency-injection-resolved services.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseDevelopment(
            Action<DevPushNotificationOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                DevPushNotificationRegistration.AddCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<DevPushNotificationOptions, DevPushNotificationOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>
        /// Selects the no-op provider, which sends and captures nothing and always reports success. Intended for
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
/// Extension members for selecting the development (capturing) or no-op push notification provider for a named
/// instance on <see cref="HeadlessPushNotificationsInstanceBuilder"/>. The instance resolves as a keyed
/// <see cref="IPushNotificationService"/> or through <see cref="IPushNotificationServiceProvider"/>.
/// </summary>
[PublicAPI]
public static class SetupDevPushNotificationsNamed
{
    extension(HeadlessPushNotificationsInstanceBuilder instance)
    {
        /// <summary>
        /// Uses the development provider with default options for this named instance. It captures each notification
        /// in <see cref="IPushNotificationCaptureStore"/> under the instance name instead of sending it.
        /// </summary>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessPushNotificationsInstanceBuilder UseDevelopment()
        {
            return instance.UseDevelopment(static _ => { });
        }

        /// <summary>
        /// Uses the development provider for this named instance, binding <see cref="DevPushNotificationOptions"/>
        /// from the supplied configuration section.
        /// </summary>
        /// <param name="config">The configuration section that maps to <see cref="DevPushNotificationOptions"/> properties.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseDevelopment(IConfiguration config)
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevPushNotificationRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevPushNotificationOptions, DevPushNotificationOptionsValidator>(config, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Uses the development provider for this named instance, configuring <see cref="DevPushNotificationOptions"/>
        /// through a delegate.
        /// </summary>
        /// <param name="configure">A delegate that populates the options.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseDevelopment(Action<DevPushNotificationOptions> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevPushNotificationRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevPushNotificationOptions, DevPushNotificationOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Uses the development provider for this named instance, configuring <see cref="DevPushNotificationOptions"/>
        /// through a delegate that also receives the <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="configure">A delegate that populates the options using dependency-injection-resolved services.</param>
        /// <returns>The instance builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseDevelopment(
            Action<DevPushNotificationOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                DevPushNotificationRegistration.AddCore(
                    services,
                    name,
                    (s, n) => s.Configure<DevPushNotificationOptions, DevPushNotificationOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>
        /// Uses the no-op provider for this named instance, sending and capturing nothing and always reporting
        /// success. Intended for development and testing only.
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

/// <summary>
/// Marks one development service registration, so the shared capture store can size itself from every service's
/// options. A <see langword="null"/> <see cref="Name"/> is the default (unkeyed) service.
/// </summary>
internal sealed record DevPushNotificationRegistration(string? Name)
{
    public static void AddCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions
    )
    {
        configureOptions(services, name);
        services.AddSingleton(new DevPushNotificationRegistration(name));

        services.TryAddSingleton(static sp =>
        {
            var monitor = sp.GetRequiredService<IOptionsMonitor<DevPushNotificationOptions>>();
            var capacity = sp.GetServices<DevPushNotificationRegistration>()
                .Max(r => monitor.Get(r.Name).CaptureCapacity);

            return new PushNotificationCaptureStore(capacity, sp.GetService<TimeProvider>() ?? TimeProvider.System);
        });

        services.TryAddSingleton<IPushNotificationCaptureStore>(static sp =>
            sp.GetRequiredService<PushNotificationCaptureStore>()
        );

        if (name is null)
        {
            services.AddSingleton<IPushNotificationService>(static sp => _CreateService(sp, name: null));

            return;
        }

        services.AddKeyedSingleton<IPushNotificationService>(name, (sp, _) => _CreateService(sp, name));
    }

    private static DevPushNotificationService _CreateService(IServiceProvider sp, string? name)
    {
        return new DevPushNotificationService(
            sp.GetRequiredService<PushNotificationCaptureStore>(),
            name ?? string.Empty,
            sp.GetService<ICorrelationIdProvider>()
        );
    }
}
