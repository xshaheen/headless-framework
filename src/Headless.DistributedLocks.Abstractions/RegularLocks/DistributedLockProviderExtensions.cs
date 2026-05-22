// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
        /// Tries to acquire a lock for a specified <paramref name="resource"/> and execute the <paramref name="work"/>.
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
        /// <param name="monitorLease">
        /// <see langword="true"/> to enable background lease monitoring. Overloads taking a
        /// <see cref="CancellationToken"/>-receiving <paramref name="work"/> delegate forward a token
        /// that is also cancelled when the lease is lost.
        /// </param>
        /// <param name="autoExtend">
        /// <see langword="true"/> to renew the lease in the background while monitoring. Implies <paramref name="monitorLease"/>.
        /// </param>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            bool monitorLease = false,
            bool autoExtend = false,
            CancellationToken cancellationToken = default
        )
        {
            await using var distributedLock = await provider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires,
                    acquireTimeout,
                    releaseOnDispose: true,
                    monitorLease: monitorLease,
                    autoExtend: autoExtend,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            // `await using` ensures DisposeAsync runs on exit — DisposeAsync drains the lease
            // monitor (cancelling its CTS) and then releases the storage row. ReleaseAsync alone
            // would leave the monitor's internal CTS un-cancelled until GC.
            await work().ConfigureAwait(false);

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,bool,bool,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            bool monitorLease = false,
            bool autoExtend = false,
            CancellationToken cancellationToken = default
        )
        {
            await using var distributedLock = await provider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires,
                    acquireTimeout,
                    releaseOnDispose: true,
                    monitorLease: monitorLease,
                    autoExtend: autoExtend,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            await work(state).ConfigureAwait(false);

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,bool,bool,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<CancellationToken, Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            bool monitorLease = false,
            bool autoExtend = false,
            CancellationToken cancellationToken = default
        )
        {
            await using var distributedLock = await provider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires,
                    acquireTimeout,
                    releaseOnDispose: true,
                    monitorLease: monitorLease,
                    autoExtend: autoExtend,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            // When monitoring is enabled, link the caller's CT with the lease-lost token so
            // the work delegate observes lease loss promptly via its CancellationToken.
            if (distributedLock.IsMonitored)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    distributedLock.HandleLostToken
                );

                await work(linkedCts.Token).ConfigureAwait(false);
            }
            else
            {
                await work(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},TimeSpan?,TimeSpan?,bool,bool,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, CancellationToken, Task> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            bool monitorLease = false,
            bool autoExtend = false,
            CancellationToken cancellationToken = default
        )
        {
            await using var distributedLock = await provider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires,
                    acquireTimeout,
                    releaseOnDispose: true,
                    monitorLease: monitorLease,
                    autoExtend: autoExtend,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            if (distributedLock.IsMonitored)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    distributedLock.HandleLostToken
                );

                await work(state, linkedCts.Token).ConfigureAwait(false);
            }
            else
            {
                await work(state, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        /// <summary>
        /// Tries to acquire a lock for <paramref name="resource"/> and runs a synchronous
        /// <paramref name="work"/> delegate. Synchronous work cannot observe a lease-lost
        /// cancellation, so this overload does NOT expose <c>monitorLease</c>/<c>autoExtend</c>.
        /// Use the <see cref="Func{Task}"/>-receiving overload when you want lease monitoring.
        /// </summary>
        public async Task<bool> TryUsingAsync(
            string resource,
            Action work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            await using var distributedLock = await provider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires,
                    acquireTimeout,
                    releaseOnDispose: true,
                    monitorLease: false,
                    autoExtend: false,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            work();

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Action,TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Action<TState> work,
            TimeSpan? timeUntilExpires = null,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            await using var distributedLock = await provider
                .TryAcquireAsync(
                    resource,
                    timeUntilExpires,
                    acquireTimeout,
                    releaseOnDispose: true,
                    monitorLease: false,
                    autoExtend: false,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            work(state);

            return true;
        }
    }
}
