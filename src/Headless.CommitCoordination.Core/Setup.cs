// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.CommitCoordination;

/// <summary>
/// Registers the core commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the core commit coordination infrastructure: the ambient scope stack,
        /// <see cref="ICurrentCommitCoordinator" />, and <see cref="ICommitScopeFactory" />.
        /// </summary>
        /// <remarks>
        /// Idempotent: repeated calls register the services at most once. Provider packages
        /// (<c>AddEntityFrameworkCommitCoordination</c>, <c>AddPostgreSqlCommitCoordination</c>, etc.) call
        /// this internally, so consuming code normally does not need to call it directly.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddCommitCoordination()
        {
            services.TryAddSingleton<CommitScopeStack>();

            // Deliberately NOT TryAdd: Messaging's setup TryAdds a null-coordinator fallback, and single-service
            // resolution returns the LAST descriptor — an unconditional registration guarantees the real
            // coordinator wins regardless of which setup call the host invokes first. A sentinel keeps the
            // last-wins ordering while stopping repeated AddCommitCoordination() calls from stacking duplicate
            // descriptors.
            if (!services.Any(static d => d.ServiceType == typeof(CommitCoordinationSentinel)))
            {
                services.AddSingleton<CommitCoordinationSentinel>();
                services.AddSingleton<ICurrentCommitCoordinator>(sp => sp.GetRequiredService<CommitScopeStack>());
            }

            services.TryAddSingleton<CommitScopeFactory>();
            services.TryAddSingleton<ICommitScopeFactory>(sp => sp.GetRequiredService<CommitScopeFactory>());

            return services;
        }
    }
}

internal sealed class CommitCoordinationSentinel;
