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
            // Deliberately NOT TryAdd: Messaging's setup TryAdds a null-coordinator fallback, and single-service
            // resolution returns the LAST descriptor — an unconditional registration guarantees the real
            // coordinator wins regardless of which setup call the host invokes first.
            services.AddSingleton<ICurrentCommitCoordinator>(sp => sp.GetRequiredService<CommitScopeStack>());
            services.TryAddSingleton<CommitScopeFactory>();
            services.TryAddSingleton<ICommitScopeFactory>(sp => sp.GetRequiredService<CommitScopeFactory>());

            return services;
        }
    }
}
