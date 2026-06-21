// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Cequens;

[PublicAPI]
public static class SetupCequens
{
    internal const string HttpClientName = "Headless:CequensSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Cequens, binding and validating <see cref="CequensSmsOptions"/> from configuration.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseCequens(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);
            setup.RegisterExtension(new CequensProviderOptionsExtension(config, configureClient, configureResilience));

            return setup;
        }

        /// <summary>Selects Cequens, configuring <see cref="CequensSmsOptions"/> via a delegate.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseCequens(
            Action<CequensSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new CequensProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }

        /// <summary>Selects Cequens, configuring <see cref="CequensSmsOptions"/> with access to the service provider.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseCequens(
            Action<CequensSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new CequensProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }
    }

    private sealed class CequensProviderOptionsExtension : ISmsProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions;
        private readonly Action<HttpClient>? _configureClient;
        private readonly Action<HttpStandardResilienceOptions>? _configureResilience;

        public CequensProviderOptionsExtension(
            IConfiguration config,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services => services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(config);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public CequensProviderOptionsExtension(
            Action<CequensSmsOptions> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public CequensProviderOptionsExtension(
            Action<CequensSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<CequensSmsOptions, CequensSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public void AddServices(IServiceCollection services)
        {
            _configureOptions(services);
            services.AddSingleton<ISmsSender, CequensSmsSender>();

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
