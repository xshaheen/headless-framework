// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency.InMemory;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Idempotency;

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
