// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Checkers;
using Framework.Permissions.Filters;
using Framework.Permissions.Testing;
using Framework.Permissions.Values;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Permissions;

[PublicAPI]
public static class AddPermissionsExtensions
{
    public static IServiceCollection AddPermissionsManagementCore(this IServiceCollection services)
    {
        // This is a fallback store, it should be replaced by a real store
        services.TryAddSingleton<IPermissionStore, NullPermissionStore>();

        return services;
    }

    public static IServiceCollection AddAlwaysAllowAuthorization(this IServiceCollection services)
    {
        services.ReplaceSingleton<IPermissionChecker, AlwaysAllowPermissionChecker>();
        services.ReplaceSingleton<IAuthorizationService, AlwaysAllowAuthorizationService>();

        services.ReplaceSingleton<
            IMethodInvocationAuthorizationService,
            AlwaysAllowMethodInvocationAuthorizationService
        >();

        return services;
    }
}
