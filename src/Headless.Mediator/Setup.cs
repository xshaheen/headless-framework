// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;
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
        /// Consumers must register any <see cref="FluentValidation.IValidator{T}" />
        /// implementations separately. Registration is idempotent.
        /// </remarks>
        /// <param name="lifetime">The behavior's service lifetime.</param>
        /// <param name="logPayloads">
        /// <see langword="true"/> to write the whole message and response objects into the Debug entries. They carry whatever
        /// the caller put in them, so leave this off unless the log pipeline redacts classified members (for example
        /// <c>Headless.Logging.Serilog</c> with <c>AddRedaction</c>). Any registration that passes <see langword="true"/>
        /// turns payloads on for every Mediator logging behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorValidationRequestBehavior(
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            bool logPayloads = false
        )
        {
            Argument.IsNotNull(services);

            MediatorPayloadLogging.Register(services, logPayloads);

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
        /// Consumers must register <see cref="ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <param name="lifetime">The behaviors' service lifetime.</param>
        /// <param name="logPayloads">
        /// <see langword="true"/> to write the whole message and response objects into the Debug entries. They carry whatever
        /// the caller put in them, so leave this off unless the log pipeline redacts classified members (for example
        /// <c>Headless.Logging.Serilog</c> with <c>AddRedaction</c>). Any registration that passes <see langword="true"/>
        /// turns payloads on for every Mediator logging behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorLoggingBehaviors(
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            bool logPayloads = false
        )
        {
            Argument.IsNotNull(services);

            services.AddMediatorRequestResponseLoggingBehaviors(lifetime, logPayloads);
            services.AddMediatorSlowRequestsLoggingBehaviors(lifetime, logPayloads);

            return services;
        }

        /// <summary>Adds the standard Mediator request, and response logging behaviors.</summary>
        /// <remarks>
        /// Consumers must register <see cref="ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <param name="lifetime">The behaviors' service lifetime.</param>
        /// <param name="logPayloads">
        /// <see langword="true"/> to write the whole message and response objects into the Debug entries. They carry whatever
        /// the caller put in them, so leave this off unless the log pipeline redacts classified members (for example
        /// <c>Headless.Logging.Serilog</c> with <c>AddRedaction</c>). Any registration that passes <see langword="true"/>
        /// turns payloads on for every Mediator logging behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorRequestResponseLoggingBehaviors(
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            bool logPayloads = false
        )
        {
            Argument.IsNotNull(services);

            MediatorPayloadLogging.Register(services, logPayloads);

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
        /// Consumers must register <see cref="ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <param name="lifetime">The behavior's service lifetime.</param>
        /// <param name="logPayloads">
        /// <see langword="true"/> to write the whole message and response objects into the Debug entries. They carry whatever
        /// the caller put in them, so leave this off unless the log pipeline redacts classified members (for example
        /// <c>Headless.Logging.Serilog</c> with <c>AddRedaction</c>). Any registration that passes <see langword="true"/>
        /// turns payloads on for every Mediator logging behavior.
        /// </param>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorSlowRequestsLoggingBehaviors(
            ServiceLifetime lifetime = ServiceLifetime.Scoped,
            bool logPayloads = false
        )
        {
            Argument.IsNotNull(services);

            MediatorPayloadLogging.Register(services, logPayloads);

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
