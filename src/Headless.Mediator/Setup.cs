// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Mediator.Behaviors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Mediator;

/// <summary>
/// Provides dependency-injection setup helpers for Headless Mediator behaviors.
/// </summary>
[PublicAPI]
public static class SetupMediator
{
    extension(IServiceCollection services)
    {
        /// <summary>Adds the FluentValidation Mediator request pre-processor.</summary>
        /// <remarks>
        /// Consumers must register any <see cref="global::FluentValidation.IValidator{T}" />
        /// implementations separately. Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorValidationRequestBehavior(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            Argument.IsNotNull(services);

            var serviceDescriptor = ServiceDescriptor.Describe(
                typeof(IPipelineBehavior<,>),
                typeof(ValidationRequestPreProcessor<,>),
                lifetime
            );

            services.TryAddEnumerable(serviceDescriptor);

            return services;
        }

        /// <summary>Adds the standard Mediator request, response, and slow-request logging behaviors.</summary>
        /// <remarks>
        /// Consumers must register <see cref="Headless.Abstractions.ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorLoggingBehaviors(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        {
            Argument.IsNotNull(services);

            services.AddMediatorRequestResponseLoggingBehaviors(lifetime);
            services.AddMediatorSlowRequestsLoggingBehaviors(lifetime);

            return services;
        }

        /// <summary>Adds the standard Mediator request, and response logging behaviors.</summary>
        /// <remarks>
        /// Consumers must register <see cref="Headless.Abstractions.ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorRequestResponseLoggingBehaviors(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            Argument.IsNotNull(services);

            services.TryAddEnumerable(
                ServiceDescriptor.Describe(typeof(IPipelineBehavior<,>), typeof(RequestLoggingBehavior<,>), lifetime)
            );

            services.TryAddEnumerable(
                ServiceDescriptor.Describe(typeof(IPipelineBehavior<,>), typeof(ResponseLoggingBehavior<,>), lifetime)
            );

            return services;
        }

        /// <summary>Adds the slow-request logging behaviors.</summary>
        /// <remarks>
        /// Consumers must register <see cref="Headless.Abstractions.ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorSlowRequestsLoggingBehaviors(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            Argument.IsNotNull(services);

            var serviceDescriptor = ServiceDescriptor.Describe(
                typeof(IPipelineBehavior<,>),
                typeof(CriticalRequestLoggingBehavior<,>),
                lifetime
            );

            services.TryAddEnumerable(serviceDescriptor);

            return services;
        }
    }
}
