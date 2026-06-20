// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs;

[PublicAPI]
public static class SetupBlobsCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless blob storage from a single setup builder. Provider packages contribute through
        /// <c>Use*</c> (default) and <c>AddNamed(…, i => i.Use*(…))</c> (named) extensions on
        /// <see cref="HeadlessBlobsSetupBuilder"/>. A default store is optional (at most one); named stores are
        /// unlimited with unique names. All contributions are deferred until the setup gates pass, so a failed
        /// setup leaves the service collection unchanged.
        /// </summary>
        /// <param name="configure">The setup action selecting the providers.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHeadlessBlobs(Action<HeadlessBlobsSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessBlobsSetupBuilder(services);
            configure(setup);

            return _AddBlobsCore(services, setup);
        }

        /// <summary>
        /// Registers <see cref="IBlobStorageProvider"/> backed by the container's keyed
        /// <see cref="IBlobStorage"/> registrations. Called by every blob provider setup, so the provider is
        /// available whenever any blob storage is registered. Safe to call multiple times.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        internal IServiceCollection AddBlobStorageProvider()
        {
            services.TryAddSingleton<IBlobStorageProvider>(provider => new KeyedServiceBlobStorageProvider(provider));

            return services;
        }
    }

    private static IServiceCollection _AddBlobsCore(IServiceCollection services, HeadlessBlobsSetupBuilder setup)
    {
        if (setup.DefaultExtensions.Count > 1)
        {
            throw new InvalidOperationException(
                "Headless.Blobs allows at most one default blob storage provider. Multiple default providers were "
                    + "configured — register the additional stores as named instances with `AddNamed`."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(BlobsProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessBlobs was already called on this service collection. Configure all blob stores "
                    + "(default and named) in a single AddHeadlessBlobs call."
            );
        }

        services.AddSingleton(new BlobsProviderRegistration());

        services.AddBlobStorageProvider();

        foreach (var action in setup.DefaultExtensions)
        {
            action(services);
        }

        foreach (var (_, action) in setup.NamedExtensions)
        {
            action(services);
        }

        foreach (var action in setup.CrossCuttingExtensions)
        {
            action(services);
        }

        return services;
    }

    private sealed record BlobsProviderRegistration;
}
