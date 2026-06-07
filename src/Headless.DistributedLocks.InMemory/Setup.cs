// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks.InMemory;

/// <summary>
/// Extension methods for registering in-process resource locks.
/// Suitable for tests, local development, and single-instance apps only.
/// </summary>
/// <remarks>
/// This provider is not distributed and does not coordinate across application instances.
/// Messaging is optional; when an <see cref="Headless.Messaging.IOutboxBus"/> registration exists before lock setup,
/// release notifications use push wake-ups. Otherwise, waiters fall back to polling backoff.
/// </remarks>
[PublicAPI]
public static class SetupInMemoryDistributedLock
{
    extension(HeadlessDistributedLocksSetupBuilder setup)
    {
        /// <summary>Adds in-process distributed-lock primitives.</summary>
        public HeadlessDistributedLocksSetupBuilder UseInMemory()
        {
            setup.RegisterExtension(new InMemoryDistributedLocksOptionsExtension());

            return setup;
        }
    }

    private sealed class InMemoryDistributedLocksOptionsExtension : IDistributedLocksOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddDistributedLockCore<InMemoryDistributedLockStorage>();
            services.AddDistributedReadWriteLockCore<InMemoryDistributedReadWriteLockStorage>();
            services.AddDistributedSemaphoreCore<InMemoryDistributedSemaphoreStorage>();
        }
    }
}
