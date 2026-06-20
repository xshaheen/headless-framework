// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.CommitCoordination.InMemory;

/// <summary>
/// Registers in-memory commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupInMemoryCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the in-memory commit signal source.
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddInMemoryCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<InMemoryCommitSignalSource>();
            services.TryAddSingleton<ICommitSignalSource>(sp => sp.GetRequiredService<InMemoryCommitSignalSource>());

            return services;
        }
    }
}
