// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.IpGeolocation;

/// <summary>Registers IP geolocation.</summary>
[PublicAPI]
public static class SetupIpGeolocation
{
    private const string _ProvidersHint = "`UseMaxMind` (Headless.IpGeolocation.MaxMind)";

    extension(IServiceCollection services)
    {
        /// <summary>Registers <see cref="IIpGeolocator" /> with exactly one provider.</summary>
        /// <param name="configure">Chooses the provider, for example <c>geo => geo.UseMaxMind(...)</c>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        /// <exception cref="InvalidOperationException">
        /// No provider or more than one provider was chosen, or IP geolocation was already registered.
        /// </exception>
        public IServiceCollection AddHeadlessIpGeolocation(Action<HeadlessIpGeolocationSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessIpGeolocationSetupBuilder(services);
            configure(setup);

            return _AddIpGeolocationCore(services, setup);
        }
    }

    private static IServiceCollection _AddIpGeolocationCore(
        IServiceCollection services,
        HeadlessIpGeolocationSetupBuilder setup
    )
    {
        if (setup.Extensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.Extensions.Count == 0
                    ? $"Headless.IpGeolocation requires exactly one provider. Call {_ProvidersHint}."
                    : $"Headless.IpGeolocation requires exactly one provider. Multiple providers were configured; call only one of {_ProvidersHint}."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(IpGeolocationRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessIpGeolocation was already called on this service collection. Configure IP geolocation "
                    + "in a single AddHeadlessIpGeolocation call."
            );
        }

        services.AddSingleton(new IpGeolocationRegistration());
        setup.Extensions[0].AddServices(services);

        return services;
    }

    private sealed record IpGeolocationRegistration;
}
