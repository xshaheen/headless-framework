// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency.InMemory;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Idempotency;

/// <summary>Chooses process memory as the idempotency provider.</summary>
[PublicAPI]
public static class SetupIdempotencyInMemory
{
    extension(HeadlessIdempotencySetupBuilder setup)
    {
        /// <summary>
        /// Keeps idempotency records in this process's memory: for tests, local development, and single-instance
        /// hosts.
        /// </summary>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// Records deduplicate only the requests this process handles and disappear when it stops; never use it
        /// behind a load balancer with several instances. Leases and retention are decided by the registered
        /// <see cref="TimeProvider" />. An enlisted call is accepted only on a resource-less unit of work, which is
        /// its commit boundary; a unit over a database connection is refused.
        /// </remarks>
        public HeadlessIdempotencySetupBuilder UseInMemory()
        {
            setup.RegisterExtension(new InMemoryIdempotencyOptionsExtension());

            return setup;
        }
    }
}

/// <summary>Registers the in-memory record store.</summary>
/// <param name="sharedStorage">
/// Record state to register instead of a new one, so several service providers in one test process share one table
/// the way several hosts share one database.
/// </param>
internal sealed class InMemoryIdempotencyOptionsExtension(InMemoryIdempotencyStorage? sharedStorage = null)
    : IIdempotencyProviderOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        // Autonomous calls run in owned units the store begins, and enlisted calls reach the store through
        // unit.Idempotency, so the unit-of-work factory must exist whether or not the host registered it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddUnitOfWork();

        if (sharedStorage is null)
        {
            services.TryAddSingleton<InMemoryIdempotencyStorage>();
        }
        else
        {
            services.TryAddSingleton(sharedStorage);
        }

        services.TryAddSingleton<IIdempotencyRecordStore, InMemoryIdempotencyRecordStore>();
    }
}
