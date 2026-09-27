// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Extension members for selecting Firebase Cloud Messaging as the default (unkeyed) push-notification
/// provider on <see cref="HeadlessPushNotificationsSetupBuilder"/>. Named instances are configured through
/// <see cref="SetupFirebasePushNotificationsNamed"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registers <see cref="IPushNotificationService"/> and <see cref="IFcmPushNotificationService"/>, both resolving to
/// one service instance: unkeyed for the default, keyed by name for a named instance.
/// </para>
/// <para>
/// The Firebase app and credentials are created lazily on the first send (not during registration), so
/// configuration errors surface through the options validator at startup rather than as registration-time
/// side effects, and several hosts can coexist in one process with different credentials.
/// </para>
/// </remarks>
[PublicAPI]
public static class SetupFirebasePushNotifications
{
    extension(HeadlessPushNotificationsSetupBuilder setup)
    {
        /// <summary>Selects Firebase, binding and validating <see cref="FirebaseOptions"/> from configuration.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
                AddFirebaseCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<FirebaseOptions, FirebaseOptionsValidator>(configuration, n)
                )
            );

            return setup;
        }

        /// <summary>Selects Firebase, configuring <see cref="FirebaseOptions"/> via a delegate.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(Action<FirebaseOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                AddFirebaseCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<FirebaseOptions, FirebaseOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>Selects Firebase, configuring <see cref="FirebaseOptions"/> with access to the service provider.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(Action<FirebaseOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterDefaultProvider(services =>
                AddFirebaseCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<FirebaseOptions, FirebaseOptionsValidator>(configure, n)
                )
            );

            return setup;
        }

        /// <summary>Selects Firebase from a pre-built options instance (validated at startup).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(FirebaseOptions options)
        {
            Argument.IsNotNull(options);

            setup.RegisterDefaultProvider(services => AddFirebaseCore(services, name: null, CopyOptions(options)));

            return setup;
        }
    }

    /// <summary>
    /// Registers the Firebase push-notification service. <paramref name="name"/> <see langword="null"/>
    /// registers the default (unkeyed) service and an overridable <see cref="IFcmMessageSender"/>
    /// (<c>TryAddSingleton</c>, so a host-supplied sender wins); a non-null name registers a keyed service and
    /// keyed sender built from that name's options. Every factory reads the options
    /// snapshot for its own name (<c>IOptionsMonitor.Get(name)</c>) so keyed settings never bleed across
    /// instances — keyed DI does not cascade the key to ctor dependencies, and a keyed sender must not read
    /// <c>CurrentValue</c> (which binds the default).
    /// </summary>
    internal static void AddFirebaseCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions
    )
    {
        configureOptions(services, name);
        services.TryAddSingleton(TimeProvider.System);

        // One instance serves both service types, so the typed and shared paths share its sender, Firebase app,
        // credentials, and options.
        if (name is null)
        {
            services.TryAddSingleton<IFcmMessageSender>(static sp => new FcmMessageSender(
                sp.GetRequiredService<IOptionsMonitor<FirebaseOptions>>(),
                optionsName: null,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<FcmMessageSender>>()
            ));

            services.AddSingleton(static sp => new FcmPushNotificationService(
                sp.GetRequiredService<IFcmMessageSender>(),
                sp.GetRequiredService<TimeProvider>()
            ));
            services.AddSingleton<IPushNotificationService>(static sp =>
                sp.GetRequiredService<FcmPushNotificationService>()
            );
            services.AddSingleton<IFcmPushNotificationService>(static sp =>
                sp.GetRequiredService<FcmPushNotificationService>()
            );

            return;
        }

        services.AddKeyedSingleton<IFcmMessageSender>(
            name,
            (sp, key) =>
                new FcmMessageSender(
                    sp.GetRequiredService<IOptionsMonitor<FirebaseOptions>>(),
                    optionsName: (string)key!,
                    sp.GetRequiredService<TimeProvider>(),
                    sp.GetRequiredService<ILogger<FcmMessageSender>>()
                )
        );

        services.AddKeyedSingleton(
            name,
            static (sp, key) =>
                new FcmPushNotificationService(
                    sp.GetRequiredKeyedService<IFcmMessageSender>(key),
                    sp.GetRequiredService<TimeProvider>()
                )
        );
        services.AddKeyedSingleton<IPushNotificationService>(
            name,
            static (sp, key) => sp.GetRequiredKeyedService<FcmPushNotificationService>(key)
        );
        services.AddKeyedSingleton<IFcmPushNotificationService>(
            name,
            static (sp, key) => sp.GetRequiredKeyedService<FcmPushNotificationService>(key)
        );
    }

    internal static Action<IServiceCollection, string?> CopyOptions(FirebaseOptions options)
    {
        return (services, name) =>
            services.Configure<FirebaseOptions, FirebaseOptionsValidator>(
                target =>
                {
                    target.Json = options.Json;
                    target.Retry = options.Retry;
                    target.TreatSenderIdMismatchAsUnregistered = options.TreatSenderIdMismatchAsUnregistered;
                },
                name
            );
    }
}

/// <summary>
/// Extension members for selecting Firebase Cloud Messaging for a named push-notification instance on
/// <see cref="HeadlessPushNotificationsInstanceBuilder"/>. The instance owns its own named options, keyed
/// <see cref="IFcmMessageSender"/>, and keyed service; it never shares them with the default service or other
/// named instances.
/// </summary>
[PublicAPI]
public static class SetupFirebasePushNotificationsNamed
{
    extension(HeadlessPushNotificationsInstanceBuilder instance)
    {
        /// <summary>Uses Firebase for this named instance, binding and validating <see cref="FirebaseOptions"/> from configuration.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseFirebase(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupFirebasePushNotifications.AddFirebaseCore(
                    services,
                    name,
                    (s, n) => s.Configure<FirebaseOptions, FirebaseOptionsValidator>(configuration, n)
                )
            );

            return instance;
        }

        /// <summary>Uses Firebase for this named instance, configuring <see cref="FirebaseOptions"/> via a delegate.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseFirebase(Action<FirebaseOptions> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupFirebasePushNotifications.AddFirebaseCore(
                    services,
                    name,
                    (s, n) => s.Configure<FirebaseOptions, FirebaseOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>Uses Firebase for this named instance, configuring <see cref="FirebaseOptions"/> with access to the service provider.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseFirebase(Action<FirebaseOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupFirebasePushNotifications.AddFirebaseCore(
                    services,
                    name,
                    (s, n) => s.Configure<FirebaseOptions, FirebaseOptionsValidator>(configure, n)
                )
            );

            return instance;
        }

        /// <summary>Uses Firebase for this named instance from a pre-built options instance (validated at startup).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsInstanceBuilder UseFirebase(FirebaseOptions options)
        {
            Argument.IsNotNull(options);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupFirebasePushNotifications.AddFirebaseCore(
                    services,
                    name,
                    SetupFirebasePushNotifications.CopyOptions(options)
                )
            );

            return instance;
        }
    }
}
