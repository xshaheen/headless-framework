// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
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
            // The cache is chosen by AttemptLimiterOptions.CacheName, which is only known once options bind, so the
            // registration check runs at startup instead of through RequireRegisteredService<ICache>.
            services.AddStartupValidator<AttemptLimiterCacheStartupValidator>();

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IAttemptLimiter>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<AttemptLimiterOptions>>();
                var cache = options.Value.CacheName is { } name
                    ? provider.GetRequiredKeyedService<ICache>(name)
                    : provider.GetRequiredService<ICache>();

                return new CacheAttemptLimiter(cache, provider.GetRequiredService<TimeProvider>(), options);
            });

            return services;
        }
    }
}
