// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

public static class SetupRateLimitingDashboard
{
    extension(HeadlessDashboardSetupBuilder dashboard)
    {
        /// <summary>
        /// Adds the Rate limits module to the unified dashboard under <see cref="RateLimitingDashboardOptions.ModuleId"/>,
        /// in the Platform product: the purposes <see cref="AttemptLimiterOptions.Purposes"/> declares, a lookup of one
        /// subject's attempts, and a confirmed reset of its current window.
        /// </summary>
        /// <remarks>
        /// The module reads the attempt limiter <c>AddAttemptLimiter</c> registered. A host that adds it without calling
        /// <c>AddAttemptLimiter</c> fails to start with a message naming that call.
        /// </remarks>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="InvalidOperationException">A module with the id <c>rate-limiting</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddRateLimiting()
        {
            return dashboard.AddRateLimiting(static _ => { });
        }

        /// <summary>Adds the Rate limits module configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>rate-limiting</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddRateLimiting(Action<RateLimitingDashboardOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<RateLimitingDashboardOptions, RateLimitingDashboardOptionsValidator>(
                setupAction
            );

            return _AddRateLimitingModule(dashboard);
        }

        /// <summary>Adds the Rate limits module configured by <paramref name="setupAction"/>, with the service provider.</summary>
        /// <param name="setupAction">Configures the module's policies.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>rate-limiting</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddRateLimiting(
            Action<RateLimitingDashboardOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            dashboard.Services.Configure<RateLimitingDashboardOptions, RateLimitingDashboardOptionsValidator>(
                setupAction
            );

            return _AddRateLimitingModule(dashboard);
        }

        /// <summary>
        /// Adds the Rate limits module bound from <paramref name="configuration"/>, with the keys <c>ViewPolicy</c> and
        /// <c>OperatePolicy</c>.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">A module with the id <c>rate-limiting</c> is already added.</exception>
        public HeadlessDashboardSetupBuilder AddRateLimiting(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            dashboard.Services.Configure<RateLimitingDashboardOptions, RateLimitingDashboardOptionsValidator>(
                configuration
            );

            return _AddRateLimitingModule(dashboard);
        }
    }

    private const string _RequiredBy = "the Rate limits dashboard module (AddRateLimiting)";
    private const string _Remedy = "Call services.AddAttemptLimiter(...) to register the Headless attempt limiter.";

    private static HeadlessDashboardSetupBuilder _AddRateLimitingModule(HeadlessDashboardSetupBuilder dashboard)
    {
        var services = dashboard.Services;

        // The module looks up and resets through the limiter, and derives every fingerprint it serves from the
        // fingerprinter AddAttemptLimiter registers beside it; without either nothing could serve a request, so the
        // host fails at start rather than on the first dashboard call.
        services.RequireRegisteredService<IAttemptLimiter>(_RequiredBy, _Remedy);
        services.RequireRegisteredService<AttemptSubjectFingerprinter>(_RequiredBy, _Remedy);

        services.TryAddSingleton(provider =>
        {
            var limiter = _Require<IAttemptLimiter>(provider);
            var limiterOptions = provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value;

            return new RateLimitingModule(
                limiter,
                _Require<AttemptSubjectFingerprinter>(provider),
                limiterOptions,
                RateLimitingCatalogueReader.Create(limiterOptions, limiter, provider)
            );
        });

        return dashboard.AddModule<RateLimitingModule, RateLimitingDashboardOptions>(
            RateLimitingDashboardOptions.ModuleId
        );
    }

    // The shell may build the module while it maps endpoints, before startup validation reports the missing call, so
    // the module names it too.
    private static T _Require<T>(IServiceProvider provider)
        where T : class =>
        provider.GetService<T>()
        ?? throw new InvalidOperationException($"{_RequiredBy} needs the Headless attempt limiter. {_Remedy}");
}
