// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
            Argument.IsNotNull(provider);

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
        /// <param name="resource">The resource to acquire the lock for.</param>
        /// <param name="work">The async work to execute while holding the lock.</param>
        /// <param name="options">
        /// Per-call configuration. See <see cref="DistributedLockAcquireOptions"/>.
        /// The extension owns the handle lifetime, so <see cref="DistributedLockAcquireOptions.ReleaseOnDispose"/>
        /// is forced to <see langword="true"/> regardless of what the caller sets.
        /// Overloads taking a <see cref="CancellationToken"/>-receiving <paramref name="work"/>
        /// delegate forward a token that is also cancelled when the lease is lost (when monitoring is enabled).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
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

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},DistributedLockAcquireOptions,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
                .ConfigureAwait(false);

            if (distributedLock is null)
            {
                return false;
            }

            await work(state).ConfigureAwait(false);

            return true;
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},DistributedLockAcquireOptions,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Func<CancellationToken, Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
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

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Func{Task},DistributedLockAcquireOptions,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Func<TState, CancellationToken, Task> work,
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with { ReleaseOnDispose = true };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
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
        /// cancellation, so this overload always passes <see cref="LockMonitoringMode.None"/>.
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
            var options = new DistributedLockAcquireOptions
            {
                TimeUntilExpires = timeUntilExpires,
                AcquireTimeout = acquireTimeout,
            };

            return await provider.TryUsingAsync(resource, work, options, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc cref="TryUsingAsync(IDistributedLockProvider,string,Action,TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync(
            string resource,
            Action work,
            DistributedLockAcquireOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with
            {
                ReleaseOnDispose = true,
                Monitoring = LockMonitoringMode.None,
            };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
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
            var options = new DistributedLockAcquireOptions
            {
                TimeUntilExpires = timeUntilExpires,
                AcquireTimeout = acquireTimeout,
            };

            return await provider.TryUsingAsync(resource, state, work, options, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc cref="TryUsingAsync{TState}(IDistributedLockProvider,string,TState,Action{TState},TimeSpan?,TimeSpan?,CancellationToken)"/>
        public async Task<bool> TryUsingAsync<TState>(
            string resource,
            TState state,
            Action<TState> work,
            DistributedLockAcquireOptions? options,
            CancellationToken cancellationToken = default
        )
        {
            options = (options ?? new DistributedLockAcquireOptions()) with
            {
                ReleaseOnDispose = true,
                Monitoring = LockMonitoringMode.None,
            };

            await using var distributedLock = await provider
                .TryAcquireAsync(resource, options, cancellationToken)
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
