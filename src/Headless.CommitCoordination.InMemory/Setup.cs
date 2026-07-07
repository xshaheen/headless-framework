// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.CommitCoordination;

/// <summary>
/// Registers in-memory commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupInMemoryCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the in-memory commit signal source and the core commit coordination services.
        /// </summary>
        /// <remarks>
        /// Use for tests or non-relational flows where the caller explicitly signals the commit or rollback
        /// outcome via <see cref="ICommitScope.SignalAsync" /> on the scope returned by
        /// <c>InMemoryCommitSignalSource.Attach</c>. Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
        public IServiceCollection AddInMemoryCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<InMemoryCommitSignalSource>();
            services.TryAddSingleton<ICommitSignalSource>(sp => sp.GetRequiredService<InMemoryCommitSignalSource>());

            return services;
        }
    }
}
