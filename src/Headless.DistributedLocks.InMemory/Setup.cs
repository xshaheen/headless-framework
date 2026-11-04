// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

/// <summary>
/// Extension methods for registering in-process resource locks.
/// Suitable for tests, local development, and single-instance apps only.
/// </summary>
/// <remarks>
/// This provider is not distributed and does not coordinate across application instances.
/// A release wakes the waiters in this process at once, without any release signal transport.
/// </remarks>
[PublicAPI]
public static class SetupDistributedLocksInMemory
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>Registers in-process distributed-lock primitives (regular locks, reader-writer locks, and semaphores).</summary>
        /// <remarks>
        /// Suitable for tests, local development, and single-instance deployments only.
        /// All three storage implementations — <see cref="InMemoryDistributedLockStorage"/>,
        /// <see cref="InMemoryDistributedReadWriteLockStorage"/>, and
        /// <see cref="InMemoryDistributedSemaphoreStorage"/> — are registered as singletons.
        /// </remarks>
        /// <returns>The same <see cref="HeadlessDistributedLocksSetupBuilder"/> instance for fluent chaining.</returns>
        public HeadlessDistributedLocksSetupBuilder UseInMemory()
        {
            setup.RegisterExtension(new InMemoryDistributedLocksOptionsExtension());

            return setup;
        }
    }

    private static IServiceCollection _AddInMemoryDistributedLocksCore(IServiceCollection services)
    {
        // Before the core registrations, whose default signal logs that other hosts poll: an in-process lock has none.
        services.AddSingleProcessReleaseSignal();
        services.AddDistributedLockCore<InMemoryDistributedLockStorage>();
        services.AddDistributedReadWriteLockCore<InMemoryDistributedReadWriteLockStorage>();
        services.AddDistributedSemaphoreCore<InMemoryDistributedSemaphoreStorage>();

        return services;
    }

    private sealed class InMemoryDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            _AddInMemoryDistributedLocksCore(services);
        }
    }
}
