// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.LiveQueries.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.LiveQueries;

/// <summary>Adds Entity Framework Core tracking to <c>AddHeadlessLiveQueries(setup =&gt; …)</c>.</summary>
/// <remarks>
/// Each call tracks one context type and keeps that type's own <see cref="LiveQueriesEntityFrameworkOptions" />, as
/// options named after the context type. Calling it again for the same context type applies the new options on top
/// and attaches nothing twice.
/// </remarks>
[PublicAPI]
public static class SetupLiveQueriesEntityFramework
{
    extension(HeadlessLiveQueriesSetupBuilder setup)
    {
        /// <summary>
        /// Tracks <typeparamref name="TContext" /> with the default options: a live query's reads through the
        /// context are recorded, and every committed write through it re-runs the live queries that read the
        /// written tables. It attaches interceptors to that context only, starts no background work and does no
        /// I/O.
        /// </summary>
        /// <typeparam name="TContext">The context type, registered with any of the <c>AddDbContext</c> family.</typeparam>
        /// <returns>The builder, to allow chaining.</returns>
        public HeadlessLiveQueriesSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            Argument.IsNotNull(setup);
            setup.Services.AddOptions<LiveQueriesEntityFrameworkOptions, LiveQueriesEntityFrameworkOptionsValidator>(
                _OptionsName<TContext>()
            );

            return setup._AddEntityFrameworkCore<TContext>();
        }

        /// <summary>
        /// Tracks <typeparamref name="TContext" /> and binds its <see cref="LiveQueriesEntityFrameworkOptions" />
        /// from <paramref name="configuration" />.
        /// </summary>
        /// <typeparam name="TContext">The context type, registered with any of the <c>AddDbContext</c> family.</typeparam>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options are invalid.</exception>
        public HeadlessLiveQueriesSetupBuilder UseEntityFramework<TContext>(IConfiguration configuration)
            where TContext : DbContext
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(configuration);
            setup.Services.Configure<LiveQueriesEntityFrameworkOptions, LiveQueriesEntityFrameworkOptionsValidator>(
                configuration,
                _OptionsName<TContext>()
            );

            return setup._AddEntityFrameworkCore<TContext>();
        }

        /// <summary>
        /// Tracks <typeparamref name="TContext" /> and configures its
        /// <see cref="LiveQueriesEntityFrameworkOptions" />.
        /// </summary>
        /// <typeparam name="TContext">The context type, registered with any of the <c>AddDbContext</c> family.</typeparam>
        /// <param name="setupAction">Delegate that mutates the options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction" /> is <see langword="null" />.</exception>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options are invalid.</exception>
        public HeadlessLiveQueriesSetupBuilder UseEntityFramework<TContext>(
            Action<LiveQueriesEntityFrameworkOptions> setupAction
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(setupAction);
            setup.Services.Configure<LiveQueriesEntityFrameworkOptions, LiveQueriesEntityFrameworkOptionsValidator>(
                setupAction,
                _OptionsName<TContext>()
            );

            return setup._AddEntityFrameworkCore<TContext>();
        }

        /// <summary>
        /// Tracks <typeparamref name="TContext" /> and configures its
        /// <see cref="LiveQueriesEntityFrameworkOptions" /> with access to the service provider.
        /// </summary>
        /// <typeparam name="TContext">The context type, registered with any of the <c>AddDbContext</c> family.</typeparam>
        /// <param name="setupAction">Delegate that mutates the options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction" /> is <see langword="null" />.</exception>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options are invalid.</exception>
        public HeadlessLiveQueriesSetupBuilder UseEntityFramework<TContext>(
            Action<LiveQueriesEntityFrameworkOptions, IServiceProvider> setupAction
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(setupAction);
            setup.Services.Configure<LiveQueriesEntityFrameworkOptions, LiveQueriesEntityFrameworkOptionsValidator>(
                setupAction,
                _OptionsName<TContext>()
            );

            return setup._AddEntityFrameworkCore<TContext>();
        }

        private HeadlessLiveQueriesSetupBuilder _AddEntityFrameworkCore<TContext>()
            where TContext : DbContext
        {
            var services = setup.Services;

            // One tracker for every tracked context type, so two contexts that share a transaction add their
            // writes to the same pending set.
            services.TryAddSingleton<LiveWriteTracker>();

            if (services.Any(descriptor => descriptor.ServiceType == typeof(LiveContextTracking<TContext>)))
            {
                return setup;
            }

            services.AddSingleton(provider => new LiveContextTracking<TContext>(
                provider
                    .GetRequiredService<IOptionsMonitor<LiveQueriesEntityFrameworkOptions>>()
                    .Get(_OptionsName<TContext>()),
                provider.GetRequiredService<LiveWriteTracker>()
            ));

            // A singleton configuration reads only singletons, so it serves a context whose options are scoped as
            // well as a pooled context or a context factory, whose options are built from the root provider.
            services.ConfigureDbContext<TContext>(
                (provider, options) =>
                    options.AddInterceptors(provider.GetRequiredService<LiveContextTracking<TContext>>().Interceptors),
                ServiceLifetime.Singleton
            );

            return setup;
        }
    }

    private static string _OptionsName<TContext>()
        where TContext : DbContext
    {
        return typeof(TContext).FullName ?? typeof(TContext).Name;
    }
}
