// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Abstractions;

/// <summary>DI registration for <see cref="IHostIdentityAccessor"/>.</summary>
[PublicAPI]
public static class SetupHostIdentity
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <see cref="IHostIdentityAccessor"/> with its build-information and GUID dependencies.
        /// Every registration is <c>TryAdd</c>, so feature packages call this for the identity they need and
        /// the host's own call, or an earlier package's, wins.
        /// </summary>
        /// <param name="configure">Optional overrides; every unset <see cref="HostIdentityOptions"/> member is discovered.</param>
        /// <returns>The same service collection for chaining.</returns>
        public IServiceCollection AddHeadlessHostIdentity(Action<HostIdentityOptions>? configure = null)
        {
            var options = new HostIdentityOptions();
            configure?.Invoke(options);

            services.AddHeadlessGuidGenerator();
            services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
            services.TryAddSingleton<IHostIdentityAccessor>(provider => new HostIdentityAccessor(
                options,
                provider.GetRequiredService<IBuildInformationAccessor>(),
                provider.GetRequiredService<IGuidGenerator>(),
                provider.GetService<ILogger<HostIdentityAccessor>>()
            ));

            return services;
        }
    }
}
