// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Headless.Tus;

[PublicAPI]
public static class TusDistributedLockSetup
{
    /// <summary>
    /// Extension method to add the <see cref="DistributedLockTusLockProvider"/> as the implementation
    /// of <see cref="ITusFileLockProvider"/> to the dependency injection container.
    /// Note: this depends on <see cref="IDistributedLockProvider"/> being registered.
    /// </summary>
    public static IServiceCollection AddDistributedLockTusLockProvider(this IServiceCollection services)
    {
        return services.AddSingleton<ITusFileLockProvider, DistributedLockTusLockProvider>();
    }
}
