// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Mediator;

/// <summary>
/// Provides dependency-injection setup helpers for Headless Mediator behaviors.
/// </summary>
[PublicAPI]
public static class MediatorSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the tenant-required Mediator pipeline behavior.
        /// </summary>
        /// <remarks>
        /// Consumers must register <see cref="Headless.Abstractions.ICurrentTenant" /> and
        /// Mediator request handlers separately. Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddTenantRequiredBehavior()
        {
            Argument.IsNotNull(services);

            services.TryAddEnumerable(
                ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(TenantRequiredBehavior<,>))
            );

            return services;
        }
    }
}
