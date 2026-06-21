// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Vodafone;

[PublicAPI]
public static class SetupVodafone
{
    internal const string HttpClientName = "Headless:VodafoneSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Vodafone Egypt, binding and validating <see cref="VodafoneSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="VodafoneSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVodafone(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);
            setup.RegisterExtension(new VodafoneProviderOptionsExtension(config, configureClient, configureResilience));

            return setup;
        }

        /// <summary>Selects Vodafone Egypt, configuring <see cref="VodafoneSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVodafone(
            Action<VodafoneSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new VodafoneProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }

        /// <summary>Selects Vodafone Egypt, configuring <see cref="VodafoneSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseVodafone(
            Action<VodafoneSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new VodafoneProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }
    }

    private sealed class VodafoneProviderOptionsExtension : ISmsProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions;
        private readonly Action<HttpClient>? _configureClient;
        private readonly Action<HttpStandardResilienceOptions>? _configureResilience;

        public VodafoneProviderOptionsExtension(
            IConfiguration config,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services => services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(config);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public VodafoneProviderOptionsExtension(
            Action<VodafoneSmsOptions> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public VodafoneProviderOptionsExtension(
            Action<VodafoneSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<VodafoneSmsOptions, VodafoneSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public void AddServices(IServiceCollection services)
        {
            _configureOptions(services);
            services.AddSingleton<ISmsSender, VodafoneSmsSender>();

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
