// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.IpGeolocation.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.IpGeolocation;

/// <summary>Chooses MaxMind GeoIP2 or GeoLite2 databases as the IP geolocation provider.</summary>
[PublicAPI]
public static class SetupMaxMind
{
    extension(HeadlessIpGeolocationSetupBuilder setup)
    {
        /// <summary>Uses MaxMind databases, binding <see cref="MaxMindOptions" /> from <paramref name="configuration" />.</summary>
        /// <param name="configuration">The section bound to <see cref="MaxMindOptions" />.</param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
        public HeadlessIpGeolocationSetupBuilder UseMaxMind(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterExtension(
                new MaxMindOptionsExtension(services => services.Configure<MaxMindOptions>(configuration))
            );

            return setup;
        }

        /// <summary>Uses MaxMind databases, configuring <see cref="MaxMindOptions" /> with a delegate.</summary>
        /// <param name="configure">Configures <see cref="MaxMindOptions" />.</param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIpGeolocationSetupBuilder UseMaxMind(Action<MaxMindOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new MaxMindOptionsExtension(services => services.Configure(configure)));

            return setup;
        }

        /// <summary>Uses MaxMind databases, configuring <see cref="MaxMindOptions" /> with a service-aware delegate.</summary>
        /// <param name="configure">Configures <see cref="MaxMindOptions" />.</param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
        public HeadlessIpGeolocationSetupBuilder UseMaxMind(Action<MaxMindOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new MaxMindOptionsExtension(services => services.Configure(configure)));

            return setup;
        }
    }

    private sealed class MaxMindOptionsExtension(Action<IServiceCollection> configure)
        : IIpGeolocationProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddOptions<MaxMindOptions, MaxMindOptionsValidator>();
            configure(services);
            services.AddOptions<MaxMindOptions>().PostConfigure<IServiceProvider>(_ApplyDefaults);

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<MaxMindDatabases>();
            services.TryAddSingleton<MaxMindDownloader>();
            services.TryAddSingleton<IIpGeolocator, MaxMindIpGeolocator>();
            services.AddHostedService<MaxMindDatabaseUpdater>();

            services.AddHttpClient(
                MaxMindOptions.HttpClientName,
                static (provider, client) =>
                {
                    var options = provider.GetRequiredService<IOptions<MaxMindOptions>>().Value;
                    client.BaseAddress = new Uri(options.DownloadEndpoint);
                    client.Timeout = options.DownloadTimeout;
                }
            );
        }

        private static void _ApplyDefaults(MaxMindOptions options, IServiceProvider services)
        {
            if (options.Locales is not { Count: > 0 })
            {
                options.Locales = ["en"];
            }

            // The working directory differs between dotnet run, an orchestrating host, and a test host, so a
            // relative directory would point somewhere different in each; the content root does not.
            var environment = services.GetService<IHostEnvironment>();

            if (
                environment is not null
                && !string.IsNullOrEmpty(options.DatabaseDirectory)
                && !Path.IsPathRooted(options.DatabaseDirectory)
            )
            {
                options.DatabaseDirectory = Path.GetFullPath(options.DatabaseDirectory, environment.ContentRootPath);
            }
        }
    }
}
