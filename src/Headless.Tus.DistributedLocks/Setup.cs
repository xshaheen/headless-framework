// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>Extension methods to back TUS file locks with Headless distributed locks.</summary>
[PublicAPI]
public static class SetupTusDistributedLocks
{
    extension(HeadlessTusSetupBuilder setup)
    {
        /// <summary>
        /// Registers <see cref="DistributedLockTusLockProvider"/> as the host's <c>ITusFileLockProvider</c>, so
        /// concurrent requests for one upload exclude each other across nodes.
        /// </summary>
        /// <param name="resourcePrefix">
        /// Distributed-lock resource-key prefix; defaults to
        /// <see cref="DistributedLockTusFileLock.DefaultResourcePrefix"/>. Give each application its own prefix
        /// when several share one <see cref="IDistributedLock"/> backend.
        /// </param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// Requires <see cref="IDistributedLock"/> to be registered separately (for example through
        /// <c>AddHeadlessDistributedLocks</c>). It also makes the expired-uploads cleanup run each pass on one node
        /// per interval: the node that takes the cleanup lease sweeps, and the others skip their ticks until the
        /// lease expires.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="resourcePrefix"/> is <see langword="null"/>.</exception>
        public HeadlessTusSetupBuilder UseDistributedLocks(
            string resourcePrefix = DistributedLockTusFileLock.DefaultResourcePrefix
        )
        {
            Argument.IsNotNull(resourcePrefix);

            setup.RegisterCrossCuttingExtension(services =>
            {
                services.AddSingleton<ITusFileLockProvider>(provider => new DistributedLockTusLockProvider(
                    provider.GetRequiredService<IDistributedLock>(),
                    resourcePrefix
                ));

                // The same lock backend single-flights the expired-upload cleanup across nodes.
                services.AddSingleton<ITusExpiredUploadsCleanupGate>(provider => new DistributedLockTusCleanupGate(
                    provider.GetRequiredService<IDistributedLock>(),
                    resourcePrefix
                ));
            });

            return setup;
        }
    }
}
