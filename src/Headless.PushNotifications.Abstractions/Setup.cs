// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications;

/// <summary>
/// Root registration entry for the push-notifications feature.
/// </summary>
[PublicAPI]
public static class SetupPushNotificationsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the push-notifications feature, selecting exactly one provider via the supplied builder
        /// callback (for example <c>setup =&gt; setup.UseFirebase(...)</c> or <c>setup =&gt; setup.UseNoop()</c>).
        /// </summary>
        /// <param name="configure">Callback that selects the provider on the builder.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Zero or more than one provider was configured, or <c>AddHeadlessPushNotifications</c> was already
        /// called on this service collection.
        /// </exception>
        public IServiceCollection AddHeadlessPushNotifications(Action<HeadlessPushNotificationsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessPushNotificationsSetupBuilder(services);
            configure(setup);

            return _AddPushNotificationsCore(services, setup);
        }
    }

    private static IServiceCollection _AddPushNotificationsCore(
        IServiceCollection services,
        HeadlessPushNotificationsSetupBuilder setup
    )
    {
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.Extensions.Count == 0
                    ? "Headless.PushNotifications requires exactly one provider. Call one of `UseFirebase` or `UseNoop`."
                    : "Headless.PushNotifications requires exactly one provider. Multiple providers were configured."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(PushNotificationsProviderRegistration)))
        {
            throw new InvalidOperationException(
                "Headless.PushNotifications requires exactly one provider. Multiple providers were configured."
            );
        }

        var extension = setup.Extensions.Single();
        var extensionTypeName = extension.GetType().FullName ?? "unknown";
        services.AddSingleton(new PushNotificationsProviderRegistration(extensionTypeName));

        extension.AddServices(services);

        return services;
    }

    private sealed record PushNotificationsProviderRegistration(string Provider);
}
