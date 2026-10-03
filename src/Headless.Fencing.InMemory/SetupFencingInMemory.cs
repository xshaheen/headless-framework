// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing.InMemory;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Fencing;

/// <summary>Chooses process memory as the fencing provider.</summary>
[PublicAPI]
public static class SetupFencingInMemory
{
    extension(HeadlessFencingSetupBuilder setup)
    {
        /// <summary>
        /// Keeps leases in this process's memory: for tests, local development, and single-instance hosts.
        /// </summary>
        /// <returns>The builder, to allow chaining.</returns>
        /// <remarks>
        /// Leases coordinate only the callers of this process and disappear when it stops; never use it for work that
        /// several processes share. Expiry is decided by the registered <see cref="TimeProvider" />. An enlisted call
        /// is accepted only on a resource-less unit of work, which is its commit boundary; a unit over a database
        /// connection is refused.
        /// </remarks>
        public HeadlessFencingSetupBuilder UseInMemory()
        {
            setup.RegisterExtension(new InMemoryFencingOptionsExtension());

            return setup;
        }
    }
}
