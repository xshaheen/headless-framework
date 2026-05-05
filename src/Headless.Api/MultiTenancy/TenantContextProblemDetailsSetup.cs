// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Registers <see cref="TenantContextExceptionHandler"/> and its options so the framework's
/// global exception pipeline maps <c>Headless.Abstractions.MissingTenantContextException</c>
/// to a normalized 400 ProblemDetails response.
/// </summary>
[PublicAPI]
public static class TenantContextProblemDetailsSetup
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the tenancy exception handler with a service-provider-aware options delegate.
        /// </summary>
        /// <remarks>
        /// Prerequisites:
        /// <list type="bullet">
        /// <item><description>
        /// Register <c>services.AddHeadlessProblemDetails()</c> as well — the handler depends on
        /// <c>IProblemDetailsCreator</c> for response construction. DI will throw at handler
        /// resolution if it's missing.
        /// </description></item>
        /// <item><description>
        /// Add <c>app.UseExceptionHandler()</c> to the request pipeline. This helper only registers
        /// the handler; pipeline middleware is the consumer's responsibility.
        /// </description></item>
        /// </list>
        /// Handler ordering: ASP.NET Core invokes <c>IExceptionHandler</c> instances in registration
        /// order. Register this helper <em>before</em> any catch-all handler that returns <c>true</c>
        /// for every exception, otherwise the catch-all swallows <c>MissingTenantContextException</c>
        /// first and the tenancy mapping never runs.
        /// </remarks>
        public IServiceCollection AddTenantContextProblemDetails(
            Action<TenantContextProblemDetailsOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<TenantContextProblemDetailsOptions, TenantContextProblemDetailsOptionsValidator>(
                setupAction
            );
            return services._AddTenantContextProblemDetailsCore();
        }

        /// <summary>
        /// Registers the tenancy exception handler with options bound from the supplied
        /// <see cref="IConfiguration"/> section. Same prerequisites and ordering rules apply as the
        /// other overloads.
        /// </summary>
        public IServiceCollection AddTenantContextProblemDetails(IConfiguration configuration)
        {
            services.Configure<TenantContextProblemDetailsOptions, TenantContextProblemDetailsOptionsValidator>(
                configuration
            );
            return services._AddTenantContextProblemDetailsCore();
        }

        /// <summary>
        /// Registers the tenancy exception handler with a simple options delegate. Same prerequisites
        /// and ordering rules apply as the other overloads.
        /// </summary>
        public IServiceCollection AddTenantContextProblemDetails(Action<TenantContextProblemDetailsOptions> setupAction)
        {
            services.Configure<TenantContextProblemDetailsOptions, TenantContextProblemDetailsOptionsValidator>(
                setupAction
            );
            return services._AddTenantContextProblemDetailsCore();
        }

        private IServiceCollection _AddTenantContextProblemDetailsCore()
        {
            // ASP.NET Core's AddExceptionHandler<T>() uses AddSingleton, which is not idempotent
            // — calling this helper twice would register the handler twice. Use TryAddEnumerable
            // directly so duplicate registrations collapse to a single descriptor.
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, TenantContextExceptionHandler>());
            return services;
        }
    }
}
