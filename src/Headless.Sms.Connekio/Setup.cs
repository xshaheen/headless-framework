// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Headless.Sms.Connekio;

[PublicAPI]
public static class SetupConnekio
{
    internal const string HttpClientName = "Headless:ConnekioSms";

    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>Selects Connekio, binding and validating <see cref="ConnekioSmsOptions"/> from configuration.</summary>
        /// <remarks>
        /// HTTP retry is disabled by default because SMS sends are not idempotent. Pass
        /// <paramref name="configureResilience"/> to opt back in.
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="ConnekioSmsOptions"/> values.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseConnekio(
            IConfiguration config,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(config);
            setup.RegisterExtension(new ConnekioProviderOptionsExtension(config, configureClient, configureResilience));

            return setup;
        }

        /// <summary>Selects Connekio, configuring <see cref="ConnekioSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseConnekio(
            Action<ConnekioSmsOptions> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new ConnekioProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }

        /// <summary>Selects Connekio, configuring <see cref="ConnekioSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="configureClient">Optional delegate to further configure the underlying <see cref="HttpClient"/>.</param>
        /// <param name="configureResilience">Optional delegate to override the default resilience pipeline.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseConnekio(
            Action<ConnekioSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient = null,
            Action<HttpStandardResilienceOptions>? configureResilience = null
        )
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(
                new ConnekioProviderOptionsExtension(setupAction, configureClient, configureResilience)
            );

            return setup;
        }
    }

    private sealed class ConnekioProviderOptionsExtension : ISmsProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions;
        private readonly Action<HttpClient>? _configureClient;
        private readonly Action<HttpStandardResilienceOptions>? _configureResilience;

        public ConnekioProviderOptionsExtension(
            IConfiguration config,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services => services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(config);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public ConnekioProviderOptionsExtension(
            Action<ConnekioSmsOptions> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public ConnekioProviderOptionsExtension(
            Action<ConnekioSmsOptions, IServiceProvider> setupAction,
            Action<HttpClient>? configureClient,
            Action<HttpStandardResilienceOptions>? configureResilience
        )
        {
            _configureOptions = services =>
                services.Configure<ConnekioSmsOptions, ConnekioSmsOptionsValidator>(setupAction);
            _configureClient = configureClient;
            _configureResilience = configureResilience;
        }

        public void AddServices(IServiceCollection services)
        {
            _configureOptions(services);
            services.AddSingleton<ISmsSender, ConnekioSmsSender>();

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
