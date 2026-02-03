// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

[PublicAPI]
public static class DistributedLockProviderExtensions
{
    extension(IDistributedLockProvider provider)
    {
        /// <summary>Releases a resource lock for <paramref name="distributedLock"/>.</summary>
        public Task ReleaseAsync(IDistributedLock distributedLock, CancellationToken cancellationToken = default)
        {
            return provider.ReleaseAsync(distributedLock.Resource, distributedLock.LockId, cancellationToken);
        }

        /// <summary>
        /// Renews a resource lock for a specified <paramref name="distributedLock"/> by extending
        /// the expiration time of the lock if it is still held to the <see cref="IDistributedLock.LockId"/>
        /// and return <see langword="true"/>, otherwise <see langword="false"/>.
        /// </summary>
        public Task RenewAsync(
            IDistributedLock distributedLock,
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        )
        {
            return provider.RenewAsync(
                distributedLock.Resource,
                distributedLock.LockId,
                timeUntilExpires,
                cancellationToken
            );
        }

        /// <summary>
        /// Tries to acquire a lock for a specified <paramref name="resource"/> and execute the <paramref name="work"/>
        /// </summary>
        /// <param name="timeUntilExpires">
        /// The amount of time until the lock expires. The allowed values are:
        /// <list type="bullet">
        /// <item><see langword="null"/>: means the default value (20 minutes).</item>
        /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 milliseconds): means infinity no expiration set.</item>
        /// <item>Value greater than 0.</item>
        /// </list>
        /// </param>
        /// <param name="acquireTimeout">
        /// The amount of time to wait for the lock to be acquired. The allowed values are:
        /// <list type="bullet">
        /// <item><see langword="null"/>: means the default value (30 seconds).</item>
        /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 millisecond): means infinity wait to acquire</item>
        /// <item>Value greater than or equal to 0.</item>
        /// </list>
        /// </param>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var distributedLock = await provider
                .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                .AnyContext();

            if (distributedLock is null)
            {
                return false;
            }

            try
            {
                await work().AnyContext();

                return true;
            }
            finally
            {
                await distributedLock.ReleaseAsync();
            }
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var distributedLock = await provider
                .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                .AnyContext();

            if (distributedLock is null)
            {
                return false;
            }

            try
            {
                await work(state).AnyContext();

                return true;
            }
            finally
            {
                await distributedLock.ReleaseAsync();
            }
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<CancellationToken, Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var distributedLock = await provider
                .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                .AnyContext();

            if (distributedLock is null)
            {
                return false;
            }

            try
            {
                await work(cancellationToken).AnyContext();

                return true;
            }
            finally
            {
                await distributedLock.ReleaseAsync();
            }
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, CancellationToken, Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var distributedLock = await provider
                .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                .AnyContext();

            if (distributedLock is null)
            {
                return false;
            }

            try
            {
                await work(state, cancellationToken).AnyContext();

                return true;
            }
            finally
            {
                await distributedLock.ReleaseAsync();
            }
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Action work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var distributedLock = await provider
                .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                .AnyContext();

            if (distributedLock is null)
            {
                return false;
            }

            try
            {
                work();

                return true;
            }
            finally
            {
                await distributedLock.ReleaseAsync();
            }
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Action<TState> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            var distributedLock = await provider
                .TryAcquireAsync(resource, timeUntilExpires, acquireTimeout, cancellationToken)
                .AnyContext();

            if (distributedLock is null)
            {
                return false;
            }

            try
            {
                work(state);

                return true;
            }
            finally
            {
                await distributedLock.ReleaseAsync();
            }
        }
    }
}
