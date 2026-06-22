// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Domain;

/// <summary>Registration entry point for the <c>Headless.Domain.LocalEventBus</c> package.</summary>
[PublicAPI]
public static class SetupLocalEventBus
{
    /// <summary>
    /// Registers the in-process <see cref="ILocalEventBus"/> backed by DI-resolved
    /// <see cref="IDomainEventHandler{TEvent}"/> handlers. Registered as scoped so handlers share the
    /// caller's scope (and its <c>DbContext</c>) when dispatched within a unit of work.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHeadlessLocalEventBus(this IServiceCollection services)
    {
        services.TryAddScoped<ILocalEventBus, ServiceProviderLocalEventBus>();

        return services;
    }
}
