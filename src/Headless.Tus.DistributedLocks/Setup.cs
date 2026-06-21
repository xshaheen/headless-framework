// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// DI registration helpers for the TUS distributed-lock add-on.
/// </summary>
[PublicAPI]
public static class SetupTusDistributedLock
{
    /// <summary>
    /// Registers <see cref="DistributedLockTusLockProvider"/> as the <c>ITusFileLockProvider</c>
    /// singleton in the DI container.
    /// </summary>
    /// <param name="services">the service collection to register into</param>
    /// <returns><paramref name="services"/> for chaining</returns>
    /// <remarks>
    /// Requires <see cref="IDistributedLock"/> to be registered separately (e.g., via a Redis or
    /// SQL Server distributed-lock provider). Call this after configuring the distributed-lock
    /// backend to ensure the dependency is available at resolution time.
    /// </remarks>
    public static IServiceCollection AddDistributedLockTusLockProvider(this IServiceCollection services)
    {
        return services.AddSingleton<ITusFileLockProvider, DistributedLockTusLockProvider>();
    }
}
