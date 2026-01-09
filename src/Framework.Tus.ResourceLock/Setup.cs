// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.ResourceLocks;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Interfaces;

namespace Framework.Tus;

[PublicAPI]
public static class TusResourceLockSetup
{
    /// <summary>
    /// Extension method to add the <see cref="ResourceLockTusLockProvider"/> as the implementation
    /// of <see cref="ITusFileLockProvider"/> to the dependency injection container.
    /// Note: this depends on <see cref="IResourceLockProvider"/> being registered.
    /// </summary>
    public static IServiceCollection AddResourceLockTusLockProvider(this IServiceCollection services)
    {
        return services.AddSingleton<ITusFileLockProvider, ResourceLockTusLockProvider>();
    }
}
