// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.CommitCoordination;

/// <summary>
/// Registers commit coordination core services.
/// </summary>
[PublicAPI]
public static class SetupCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the core commit coordination services.
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddCommitCoordination()
        {
            services.TryAddSingleton<CommitScopeStack>();
            services.TryAddSingleton<ICurrentCommitCoordinator>(sp => sp.GetRequiredService<CommitScopeStack>());
            services.TryAddSingleton<CommitScopeFactory>();

            return services;
        }
    }
}
