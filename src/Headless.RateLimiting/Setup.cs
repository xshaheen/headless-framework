// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

/// <summary>Service-collection extensions that register <see cref="IAttemptLimiter"/>.</summary>
[PublicAPI]
public static class SetupAttemptLimiter
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IAttemptLimiter"/> and binds <see cref="AttemptLimiterOptions"/> from the supplied
        /// configuration section. Options are validated at host startup.
        /// </summary>
        /// <param name="configuration">The configuration section to bind to <see cref="AttemptLimiterOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="AttemptLimiterOptions.SubjectKey"/> is missing or shorter than
        /// <see cref="AttemptLimiterOptions.MinSubjectKeyBytes"/>.
        /// </exception>
        public IServiceCollection AddAttemptLimiter(IConfiguration configuration)
        {
            services.Configure<AttemptLimiterOptions, AttemptLimiterOptionsValidator>(configuration);
            return services._AddAttemptLimiterCore();
        }

        /// <summary>
        /// Registers <see cref="IAttemptLimiter"/> and configures <see cref="AttemptLimiterOptions"/> through the
        /// supplied delegate. Options are validated at host startup.
        /// </summary>
        /// <param name="setupAction">Delegate that configures <see cref="AttemptLimiterOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="AttemptLimiterOptions.SubjectKey"/> is missing or shorter than
        /// <see cref="AttemptLimiterOptions.MinSubjectKeyBytes"/>.
        /// </exception>
        public IServiceCollection AddAttemptLimiter(Action<AttemptLimiterOptions> setupAction)
        {
            services.Configure<AttemptLimiterOptions, AttemptLimiterOptionsValidator>(setupAction);
            return services._AddAttemptLimiterCore();
        }

        /// <summary>
        /// Registers <see cref="IAttemptLimiter"/> and configures <see cref="AttemptLimiterOptions"/> through the
        /// supplied delegate, which receives the <see cref="IServiceProvider"/> to read a secret store. Options are
        /// validated at host startup.
        /// </summary>
        /// <param name="setupAction">Delegate that configures <see cref="AttemptLimiterOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="AttemptLimiterOptions.SubjectKey"/> is missing or shorter than
        /// <see cref="AttemptLimiterOptions.MinSubjectKeyBytes"/>.
        /// </exception>
        public IServiceCollection AddAttemptLimiter(Action<AttemptLimiterOptions, IServiceProvider> setupAction)
        {
            services.Configure<AttemptLimiterOptions, AttemptLimiterOptionsValidator>(setupAction);
            return services._AddAttemptLimiterCore();
        }

        private IServiceCollection _AddAttemptLimiterCore()
        {
            // The limiter only counts through ICache; this package references the abstraction, so a caching
            // provider must be installed or every attempt would fail at the first request.
            services.RequireRegisteredService<ICache>(
                requiredBy: "Headless attempt limiter counters",
                remedy: "Call AddHeadlessCaching(...) with a shared provider (UseRedis / UseHybrid); UseInMemory "
                    + "counts per process."
            );

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IAttemptLimiter, CacheAttemptLimiter>();

            return services;
        }
    }
}
