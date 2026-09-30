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

        /// <summary>
        /// Wraps the default blob store and every named store with <paramref name="decoration"/>. The decoration is
        /// applied after every provider and cross-cutting extension of <c>AddHeadlessBlobs</c>, so it sees the
        /// finished store, whichever of the two calls runs first. Decorations apply in registration order; a later
        /// one wraps an earlier one.
        /// </summary>
        /// <param name="decoration">The decoration to apply.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        /// Only stores registered through <c>AddHeadlessBlobs</c> are decorated. Registering the same
        /// <see cref="BlobStorageDecoration"/> instance again is a no-op.
        /// </remarks>
        public IServiceCollection DecorateHeadlessBlobs(BlobStorageDecoration decoration)
        {
            Argument.IsNotNull(decoration);

            if (services.Any(d => ReferenceEquals(d.ImplementationInstance, decoration)))
            {
                return services;
            }

            services.AddSingleton(decoration);

            // When AddHeadlessBlobs already ran its stores are registered and are decorated now; otherwise
            // AddHeadlessBlobs applies the decoration once its providers are registered.
            if (_IsRegistered(services))
            {
                decoration.Apply(services);
            }

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

        if (_IsRegistered(services))
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

        foreach (
            var decoration in services
                .Select(static d => d.ImplementationInstance)
                .OfType<BlobStorageDecoration>()
                .ToList()
        )
        {
            decoration.Apply(services);
        }

        return services;
    }

    private static bool _IsRegistered(IServiceCollection services)
    {
        return services.Any(static descriptor => descriptor.ServiceType == typeof(BlobsProviderRegistration));
    }

    private sealed record BlobsProviderRegistration;
}
