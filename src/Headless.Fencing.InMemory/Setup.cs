// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing.InMemory;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Fencing;

/// <summary>Chooses process memory as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingInMemory
{
    extension(HeadlessFencingSetupBuilder setup)
    {
        /// <summary>
        /// Keeps leases in this process's memory: for tests, local development, and single-instance hosts.
        /// </summary>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// Leases coordinate only the callers of this process and disappear when it stops; never use it for work that
        /// several processes share. Expiry is decided by the registered <see cref="TimeProvider" />. An enlisted call
        /// is accepted only on a resource-less unit of work, which is its commit boundary; a unit over a database
        /// connection is refused.
        /// </remarks>
        public HeadlessFencingSetupBuilder UseInMemory()
        {
            setup.RegisterExtension(new InMemoryFencingOptionsExtension());

            return setup;
        }
    }
}

/// <summary>Registers the in-memory lease store.</summary>
/// <param name="sharedStorage">
/// Lease state to register instead of a new one, so several service providers in one test process share one table
/// the way several hosts share one database.
/// </param>
internal sealed class InMemoryFencingOptionsExtension(InMemoryLeaseStorage? sharedStorage = null)
    : IFencingProviderOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        // The store's clock decides expiry, and sweeps and enlisted calls go through units of work, so both must
        // exist whether or not the host registered them.
        services.TryAddSingleton(TimeProvider.System);
        services.AddUnitOfWork();

        if (sharedStorage is null)
        {
            services.TryAddSingleton<InMemoryLeaseStorage>();
        }
        else
        {
            services.TryAddSingleton(sharedStorage);
        }

        services.TryAddSingleton<ILeaseStore, InMemoryLeaseStore>();
    }
}
