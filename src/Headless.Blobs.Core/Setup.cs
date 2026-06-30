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
        /// unlimited with unique names. All contributions are deferred until the setup gates (at-most-one-default
        /// and called-once) run; if a <em>gate</em> fails the service collection is left unchanged. Once the gates
        /// pass the queued contributions are applied in order — a contribution that throws after the gates pass may
        /// leave earlier registrations (including the called-once marker) in place.
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

        var registeredNames = setup.InstanceNames.ToFrozenSet(StringComparer.Ordinal);
        services.TryAddSingleton<IBlobStorageProvider>(provider => new KeyedServiceBlobStorageProvider(
            provider,
            registeredNames
        ));

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
