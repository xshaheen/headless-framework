// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Domain;

/// <summary>Provides registration methods for the domain event dispatcher.</summary>
[PublicAPI]
public static class SetupDomainEventDispatcher
{
    /// <summary>
    /// Registers the in-process <see cref="IDomainEventDispatcher"/> backed by resolved
    /// <see cref="IDomainEventHandler{TEvent}"/> handlers.
    /// </summary>
    /// <remarks>
    /// Registered as scoped so handlers share the caller scope and its database context when dispatched within a unit of work.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddHeadlessDomainEventDispatcher(this IServiceCollection services)
    {
        services.TryAddScoped<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();

        return services;
    }
}
