// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Sms;

/// <summary>
/// Provides extension methods for registering Headless SMS senders.
/// </summary>
[PublicAPI]
public static class SetupSmsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers SMS services and configured providers with the dependency injection container.
        /// </summary>
        /// <param name="configure">A delegate that configures SMS providers.</param>
        /// <returns>The service collection instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// More than one default provider is registered, a named instance is misconfigured, an instance name is duplicated,
        /// or SMS setup was already called on this service collection.
        /// </exception>
        public IServiceCollection AddHeadlessSms(Action<HeadlessSmsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessSmsSetupBuilder(services);
            configure(setup);

            return _AddSmsProviderCore(services, setup);
        }
    }

    private static IServiceCollection _AddSmsProviderCore(IServiceCollection services, HeadlessSmsSetupBuilder setup)
    {
        if (setup.DefaultExtensions.Count > 1)
        {
            throw new InvalidOperationException(
                "Headless.Sms allows at most one default SMS provider. Multiple default providers were "
                    + "configured — register the additional senders as named instances with `AddNamed`."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(SmsProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessSms was already called on this service collection. Configure all SMS senders "
                    + "(default and named) in a single AddHeadlessSms call."
            );
        }

        services.AddSingleton(new SmsProviderRegistration());

        var registeredNames = setup.InstanceNames.ToFrozenSet(StringComparer.Ordinal);
        services.TryAddSingleton<ISmsSenderProvider>(provider => new KeyedServiceSmsSenderProvider(
            provider,
            registeredNames
        ));

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

    private sealed record SmsProviderRegistration;
}
