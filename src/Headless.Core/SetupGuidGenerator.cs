// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Core;

/// <summary>
/// DI registration extensions for the framework GUID generator.
/// </summary>
[PublicAPI]
public static class SetupGuidGenerator
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the framework GUID generator strategies. Persisted backends can resolve
        /// <see cref="SequentialGuidType.Version7"/> or <see cref="SequentialGuidType.SqlServer"/> by key, while
        /// backend-agnostic consumers use the unkeyed <see cref="IGuidGenerator"/> default.
        /// </summary>
        /// <param name="defaultType">The strategy registered for unkeyed <see cref="IGuidGenerator"/> resolution.</param>
        /// <returns>The same service collection for chaining.</returns>
        public IServiceCollection AddHeadlessGuidGenerator(SequentialGuidType defaultType = SequentialGuidType.Version7)
        {
            services.TryAddKeyedSingleton<IGuidGenerator>(
                SequentialGuidType.Version7,
                static (_, _) => new SequentialGuidGenerator(SequentialGuidType.Version7)
            );
            services.TryAddKeyedSingleton<IGuidGenerator>(
                SequentialGuidType.SqlServer,
                static (_, _) => new SequentialGuidGenerator(SequentialGuidType.SqlServer)
            );
            services.TryAddSingleton<IGuidGenerator>(new SequentialGuidGenerator(defaultType));

            return services;
        }
    }
}
