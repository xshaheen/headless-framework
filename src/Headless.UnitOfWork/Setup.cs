// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Headless.UnitOfWork.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.UnitOfWork;

/// <summary>
/// Registers the unit-of-work services.
/// </summary>
[PublicAPI]
public static class SetupUnitOfWork
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the singleton <see cref="IUnitOfWorkFactory" />.
        /// </summary>
        /// <remarks>
        /// Idempotent: repeated calls register the factory at most once. Consumer packages
        /// (<c>Headless.EntityFramework</c>, <c>Headless.Messaging</c>, <c>Headless.Jobs</c>) call
        /// this internally, so exactly one registration exists regardless of which setup the host invokes
        /// first. The factory holds no per-scope state, so any service — a singleton or hosted service included —
        /// may take it directly. A feature reached through <see cref="IUnitOfWork.GetFeature{TFeature}" /> must be
        /// registered as a singleton on this same collection; the factory refuses a scoped or transient one. The
        /// replay policy of the per-attempt-connection <c>RunAsync</c> overloads comes from
        /// <see cref="UnitOfWorkRetryOptions" />, configured with <c>services.Configure&lt;UnitOfWorkRetryOptions&gt;(…)</c>.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddUnitOfWork()
        {
            // The factory reads feature lifetimes from this collection so a scoped or transient feature is
            // refused at GetFeature in every environment, not only where scope validation is switched on.
            services.AddOptions();
            services.TryAddSingleton<IUnitOfWorkFactory>(provider => new UnitOfWorkFactory(
                provider.GetService<ILogger<UnitOfWorkFactory>>(),
                provider,
                new UnitOfWorkFeatureLifetimes(services),
                provider.GetRequiredService<IOptions<UnitOfWorkRetryOptions>>().Value
            ));

            return services;
        }
    }
}
