// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Grants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions.Testing;

/// <summary>
/// Extension methods for registering permission test doubles that bypass all authorization checks.
/// </summary>
[PublicAPI]
public static class SetupPermissionsTesting
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Replaces <see cref="IPermissionManager"/> with <see cref="AlwaysAllowPermissionManager"/> and
        /// <see cref="IAuthorizationService"/> with <see cref="AlwaysAllowAuthorizationService"/>, bypassing all
        /// permission and authorization checks. Intended for integration tests only; do not call in production.
        /// </summary>
        public IServiceCollection AddAlwaysAllowAuthorization()
        {
            services.AddOrReplaceSingleton<IPermissionManager, AlwaysAllowPermissionManager>();
            services.AddOrReplaceSingleton<IAuthorizationService, AlwaysAllowAuthorizationService>();

            return services;
        }
    }
}
