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
        /// <summary>Selects Vodafone, binding and validating <see cref="VodafoneSmsOptions"/> from configuration.</summary>
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

        /// <summary>Selects Vodafone, configuring <see cref="VodafoneSmsOptions"/> via a delegate.</summary>
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

        /// <summary>Selects Vodafone, configuring <see cref="VodafoneSmsOptions"/> with access to the service provider.</summary>
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
