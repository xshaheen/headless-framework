// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing.InMemory;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Fencing;

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
