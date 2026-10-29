// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// Extension members on <see cref="IServiceCollection"/> that configure the same-origin guard applied by
/// <c>RequireSameOrigin()</c> and <see cref="RequireSameOriginAttribute"/>.
/// </summary>
/// <remarks>
/// The guard runs without this registration, admitting only the API's own origin. Register it to list trusted origins
/// or require a header, and to have the options validated at startup.
/// </remarks>
[PublicAPI]
public static class SetupSameOrigin
{
    extension(IServiceCollection services)
    {
        /// <summary>Binds <see cref="HeadlessSameOriginOptions"/> from <paramref name="configuration"/>.</summary>
        /// <param name="configuration">The section bound to <see cref="HeadlessSameOriginOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="HeadlessSameOriginOptions"/> fails validation.
        /// </exception>
        public IServiceCollection AddHeadlessSameOriginGuard(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            return services.Configure<HeadlessSameOriginOptions, HeadlessSameOriginOptionsValidator>(configuration);
        }

        /// <summary>Configures <see cref="HeadlessSameOriginOptions"/> with <paramref name="setupAction"/>.</summary>
        /// <param name="setupAction">Configures <see cref="HeadlessSameOriginOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="HeadlessSameOriginOptions"/> fails validation.
        /// </exception>
        public IServiceCollection AddHeadlessSameOriginGuard(Action<HeadlessSameOriginOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            return services.Configure<HeadlessSameOriginOptions, HeadlessSameOriginOptionsValidator>(setupAction);
        }

        /// <summary>
        /// Configures <see cref="HeadlessSameOriginOptions"/> with <paramref name="setupAction"/>, which receives the
        /// application's <see cref="IServiceProvider"/>.
        /// </summary>
        /// <param name="setupAction">Configures <see cref="HeadlessSameOriginOptions"/>.</param>
        /// <returns>The same service collection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        /// <exception cref="OptionsValidationException">
        /// Thrown at host startup when <see cref="HeadlessSameOriginOptions"/> fails validation.
        /// </exception>
        public IServiceCollection AddHeadlessSameOriginGuard(
            Action<HeadlessSameOriginOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            return services.Configure<HeadlessSameOriginOptions, HeadlessSameOriginOptionsValidator>(setupAction);
        }
    }
}
