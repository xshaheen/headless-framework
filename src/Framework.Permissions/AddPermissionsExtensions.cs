// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Permissions.Checkers;
using Framework.Permissions.Permissions.Values;
using Framework.Permissions.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Permissions;

[PublicAPI]
public static class AddPermissionsExtensions
{
    public static IHostApplicationBuilder AddFrameworkPermissions(this IHostApplicationBuilder builder)
    {
        // This is a fallback store, it should be replaced by a real store
        builder.Services.TryAddSingleton<IPermissionStore, NullPermissionStore>();

        return builder;
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
