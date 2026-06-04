// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
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
    extension(IServiceCollection services)
    {
        #region InMemory Distributed Lock

        /// <summary>Adds an in-process resource lock provider.</summary>
        public IServiceCollection AddInMemoryDistributedLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedLock<InMemoryDistributedLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds an in-process resource lock provider.</summary>
        public IServiceCollection AddInMemoryDistributedLock(
            Action<DistributedLockOptions>? optionSetupAction = null
        )
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedLock<InMemoryDistributedLockStorage>(optionSetupAction ?? (static _ => { }))
            );
        }

        /// <summary>Adds an in-process resource lock provider.</summary>
        public IServiceCollection AddInMemoryDistributedLock(IConfiguration config)
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedLock<InMemoryDistributedLockStorage>(config)
            );
        }

        #endregion

        #region InMemory Distributed Semaphore

        /// <summary>Adds an in-process semaphore provider.</summary>
        public IServiceCollection AddInMemoryDistributedSemaphore(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedSemaphore<InMemoryDistributedSemaphoreStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds an in-process semaphore provider.</summary>
        public IServiceCollection AddInMemoryDistributedSemaphore(Action<DistributedLockOptions> optionSetupAction)
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedSemaphore<InMemoryDistributedSemaphoreStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds an in-process semaphore provider.</summary>
        public IServiceCollection AddInMemoryDistributedSemaphore(IConfiguration config)
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedSemaphore<InMemoryDistributedSemaphoreStorage>(config)
            );
        }

        #endregion

        #region InMemory Distributed Reader-Writer Lock

        /// <summary>Adds an in-process reader-writer lock provider.</summary>
        public IServiceCollection AddInMemoryDistributedReaderWriterLock(
            Action<DistributedLockOptions, IServiceProvider> optionSetupAction
        )
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds an in-process reader-writer lock provider.</summary>
        public IServiceCollection AddInMemoryDistributedReaderWriterLock(
            Action<DistributedLockOptions> optionSetupAction
        )
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(optionSetupAction)
            );
        }

        /// <summary>Adds an in-process reader-writer lock provider.</summary>
        public IServiceCollection AddInMemoryDistributedReaderWriterLock(IConfiguration config)
        {
            return services._AddInMemoryDistributedLockCore(s =>
                s.AddDistributedReaderWriterLock<InMemoryDistributedReaderWriterLockStorage>(config)
            );
        }

        #endregion

        private IServiceCollection _AddInMemoryDistributedLockCore(
            Func<IServiceCollection, IServiceCollection> registerStorage
        )
        {
            return registerStorage(services);
        }
    }
}
