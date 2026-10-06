// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Registers the DeviceDetector.NET-backed <see cref="IUserAgentParser"/>.</summary>
[PublicAPI]
public static class SetupUserAgent
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the user-agent parser with default <see cref="UserAgentParserOptions"/>.</summary>
        /// <returns>The same <see cref="IServiceCollection"/>.</returns>
        public IServiceCollection AddHeadlessUserAgentParser()
        {
            services.AddOptions<UserAgentParserOptions, UserAgentParserOptionsValidator>();

            return services._AddUserAgentParserCore();
        }

        /// <summary>Registers the user-agent parser with options bound from <paramref name="configuration"/>.</summary>
        /// <param name="configuration">The configuration section bound to <see cref="UserAgentParserOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/>.</returns>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options fail validation.</exception>
        public IServiceCollection AddHeadlessUserAgentParser(IConfiguration configuration)
        {
            services.Configure<UserAgentParserOptions, UserAgentParserOptionsValidator>(configuration);

            return services._AddUserAgentParserCore();
        }

        /// <summary>Registers the user-agent parser with options configured by <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures <see cref="UserAgentParserOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/>.</returns>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options fail validation.</exception>
        public IServiceCollection AddHeadlessUserAgentParser(Action<UserAgentParserOptions> setupAction)
        {
            services.Configure<UserAgentParserOptions, UserAgentParserOptionsValidator>(setupAction);

            return services._AddUserAgentParserCore();
        }

        /// <summary>
        /// Registers the user-agent parser with options configured by <paramref name="setupAction"/>, which receives the
        /// application's <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="setupAction">Configures <see cref="UserAgentParserOptions"/>.</param>
        /// <returns>The same <see cref="IServiceCollection"/>.</returns>
        /// <exception cref="OptionsValidationException">Thrown at host startup when the options fail validation.</exception>
        public IServiceCollection AddHeadlessUserAgentParser(
            Action<UserAgentParserOptions, IServiceProvider> setupAction
        )
        {
            services.Configure<UserAgentParserOptions, UserAgentParserOptionsValidator>(setupAction);

            return services._AddUserAgentParserCore();
        }

        private IServiceCollection _AddUserAgentParserCore()
        {
            // Replaces the identify-nothing fallback AddHeadless registers, whichever of the two calls runs first.
            services.AddOrReplaceSingleton<IUserAgentParser, UserAgentParser>();

            return services;
        }
    }
}
