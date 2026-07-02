// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.VictoryLink;

[PublicAPI]
public static class SetupVictoryLink
{
    internal const string HttpClientName = "Headless:VictoryLinkSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects VictoryLink, binding and validating <see cref="VictoryLinkSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="VictoryLinkSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVictoryLink(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);
            setup.RegisterExtension(
                new VictoryLinkProviderOptionsExtension(config, configureClient, configureResilience)
            );

            return setup;
        }

        /// <summary>Selects VictoryLink, configuring <see cref="VictoryLinkSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVictoryLink(
            Action<VictoryLinkSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new VictoryLinkProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }

        /// <summary>Selects VictoryLink, configuring <see cref="VictoryLinkSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVictoryLink(
            Action<VictoryLinkSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new VictoryLinkProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }
    }

    private sealed class VictoryLinkProviderOptionsExtension : ISmsProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions;
        private readonly Action<HttpClient>? _configureClient;
        private readonly Action<HttpStandardResilienceOptions>? _configureResilience;

        public VictoryLinkProviderOptionsExtension(
            IConfiguration config,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(config);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public VictoryLinkProviderOptionsExtension(
            Action<VictoryLinkSmsOptions> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public VictoryLinkProviderOptionsExtension(
            Action<VictoryLinkSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<VictoryLinkSmsOptions, VictoryLinkSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public void AddServices(IServiceCollection services)
        {
            _configureOptions(services);
            services.AddSingleton<ISmsSender, VictoryLinkSmsSender>();
            services.AddSingleton<IBulkSmsSender>(static sp => (IBulkSmsSender)sp.GetRequiredService<ISmsSender>());

            var httpClientBuilder = _configureClient is null
                ? services.AddHttpClient(HttpClientName)
                : services.AddHttpClient(HttpClientName, _configureClient);

            // SMS sends are not idempotent: don't auto-retry by default to avoid duplicate messages.
            // Consumers can opt back in via configureResilience (ideally with a provider idempotency key).
            httpClientBuilder.AddStandardResilienceHandler(options =>
            {
                options.Retry.ShouldHandle = static _ => PredicateResult.False();
                _configureResilience?.Invoke(options);
            });
        }
    }
}
