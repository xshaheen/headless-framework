// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Checks;
using Headless.Hosting;
using Headless.LiveQueries.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.LiveQueries;

/// <summary>Adds live endpoints to <c>AddHeadlessLiveQueries(setup =&gt; …)</c>.</summary>
/// <remarks>
/// Live endpoints write their refusals and failure events as problem details, so the host must also register
/// <see cref="IProblemDetailsCreator" />, which <c>AddHeadless()</c> and <c>AddHeadlessProblemDetails()</c> do. The
/// host fails at startup when it is missing.
/// </remarks>
[PublicAPI]
public static class SetupLiveQueriesEndpoints
{
    extension(HeadlessLiveQueriesSetupBuilder setup)
    {
        /// <summary>
        /// Registers what <c>WithLiveUpdates()</c> needs, with the default <see cref="LiveEndpointsOptions" />. It
        /// starts no background work and does no I/O.
        /// </summary>
        /// <returns>The builder, to allow chaining.</returns>
        public HeadlessLiveQueriesSetupBuilder UseEndpoints()
        {
            Argument.IsNotNull(setup);
            setup.Services.AddOptions<LiveEndpointsOptions, LiveEndpointsOptionsValidator>();

            return setup._AddEndpointsCore();
        }

        /// <summary>
        /// Registers what <c>WithLiveUpdates()</c> needs and binds <see cref="LiveEndpointsOptions" /> from
        /// <paramref name="configuration" />.
        /// </summary>
        /// <param name="configuration">The configuration section to bind.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options are invalid.</exception>
        public HeadlessLiveQueriesSetupBuilder UseEndpoints(IConfiguration configuration)
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(configuration);
            setup.Services.Configure<LiveEndpointsOptions, LiveEndpointsOptionsValidator>(configuration);

            return setup._AddEndpointsCore();
        }

        /// <summary>
        /// Registers what <c>WithLiveUpdates()</c> needs and configures <see cref="LiveEndpointsOptions" />.
        /// </summary>
        /// <param name="setupAction">Delegate that mutates the options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction" /> is <see langword="null" />.</exception>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options are invalid.</exception>
        public HeadlessLiveQueriesSetupBuilder UseEndpoints(Action<LiveEndpointsOptions> setupAction)
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(setupAction);
            setup.Services.Configure<LiveEndpointsOptions, LiveEndpointsOptionsValidator>(setupAction);

            return setup._AddEndpointsCore();
        }

        /// <summary>
        /// Registers what <c>WithLiveUpdates()</c> needs and configures <see cref="LiveEndpointsOptions" /> with
        /// access to the service provider.
        /// </summary>
        /// <param name="setupAction">Delegate that mutates the options.</param>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction" /> is <see langword="null" />.</exception>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options are invalid.</exception>
        public HeadlessLiveQueriesSetupBuilder UseEndpoints(Action<LiveEndpointsOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setup);
            Argument.IsNotNull(setupAction);
            setup.Services.Configure<LiveEndpointsOptions, LiveEndpointsOptionsValidator>(setupAction);

            return setup._AddEndpointsCore();
        }

        /// <summary>
        /// Adds a transport ahead of the built-in server-sent events and newline-delimited JSON transports, so it
        /// answers every request it accepts, including one a built-in transport would also accept. Transports
        /// added by several calls are asked in the order they were added. Endpoints need no change.
        /// </summary>
        /// <typeparam name="TTransport">The transport. It is registered as a singleton.</typeparam>
        /// <returns>The builder, to allow chaining.</returns>
        public HeadlessLiveQueriesSetupBuilder AddTransport<TTransport>()
            where TTransport : class, ILiveTransport
        {
            Argument.IsNotNull(setup);
            setup.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILiveTransport, TTransport>());

            return setup;
        }

        private HeadlessLiveQueriesSetupBuilder _AddEndpointsCore()
        {
            var services = setup.Services;

            // Wrapped by the first call only: a second one would wrap the wrapper.
            if (!services.IsAdded<LiveStreamHandler>())
            {
                // A run answers the accessor with its own request, which is how the current-user accessor sees the
                // run's freshly authenticated principal. Whatever accessor the host registered so far is wrapped,
                // and AddHttpContextAccessor() called after this adds nothing; the validator refuses a host that
                // replaces the wrapper later.
                services.AddHttpContextAccessor();
                services.DecorateWith<IHttpContextAccessor>(static (inner, _) => new LiveRunHttpContextAccessor(inner));
                services.AddStartupValidator<LiveRunHttpContextAccessorValidator>();
            }

            services.TryAddKeyedSingleton(LiveEndpointsOptions.RerunClockServiceKey, TimeProvider.System);
            services.TryAddSingleton<LiveEndpointsMetrics>();
            services.TryAddSingleton<LiveStreamLimiter>();
            services.TryAddSingleton<ServerSentEventsLiveTransport>();
            services.TryAddSingleton<NdjsonLiveTransport>();
            services.TryAddSingleton<LiveTransportSelector>();
            services.TryAddSingleton<LiveStreamHandler>();

            services.RequireRegisteredService<IProblemDetailsCreator>(
                requiredBy: "Headless live endpoints",
                remedy: "Call builder.AddHeadless() or services.AddHeadlessProblemDetails()."
            );

            return setup;
        }
    }
}
