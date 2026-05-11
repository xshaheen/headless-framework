// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
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
    /// <summary>Configures Mediator tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The Mediator tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Mediator(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessMediatorTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessMediatorTenancyBuilder(builder));

        return builder;
    }

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

        /// <summary>
        /// Adds the FluentValidation Mediator request pre-processor.
        /// </summary>
        /// <remarks>
        /// Consumers must register any <see cref="FluentValidation.IValidator{T}" />
        /// implementations separately. Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddValidationRequestPreProcessor()
        {
            Argument.IsNotNull(services);

            services.TryAddEnumerable(
                ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(ValidationRequestPreProcessor<,>))
            );

            return services;
        }

        /// <summary>
        /// Adds the standard Mediator request, response, and slow-request logging behaviors.
        /// </summary>
        /// <remarks>
        /// Consumers must register <see cref="Headless.Abstractions.ICurrentUser" /> separately.
        /// Registration is idempotent.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> instance.</returns>
        public IServiceCollection AddMediatorLoggingBehaviors()
        {
            Argument.IsNotNull(services);

            services.TryAddEnumerable(
                ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(RequestLoggingBehavior<,>))
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(ResponseLoggingBehavior<,>))
            );
            services.TryAddEnumerable(
                ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(CriticalRequestLoggingBehavior<,>))
            );

            return services;
        }
    }
}

/// <summary>Records tenant posture for Mediator request handling.</summary>
public sealed class HeadlessMediatorTenancyBuilder
{
    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessMediatorTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Adds the tenant-required Mediator pipeline behavior.</summary>
    /// <returns>The same Mediator tenancy builder.</returns>
    public HeadlessMediatorTenancyBuilder RequireTenant()
    {
        _builder.Services.AddTenantRequiredBehavior();
        _builder.RecordSeam("Mediator", TenantPostureStatuses.Enforcing, "require-tenant");

        return this;
    }
}
