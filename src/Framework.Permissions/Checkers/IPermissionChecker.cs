// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.Checkers;

[PublicAPI]
public interface IPermissionChecker
{
    Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default);

    Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names, CancellationToken cancellationToken = default);

    Task<bool> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string name,
        CancellationToken cancellationToken = default
    );

    Task<MultiplePermissionGrantResult> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string[] names,
        CancellationToken cancellationToken = default
    );
}

public sealed class PermissionChecker(
    ICurrentPrincipalAccessor principalAccessor,
    IPermissionDefinitionManager permissionDefinitionManager,
    ICurrentTenant currentTenant,
    IPermissionValueProviderManager permissionValueProviderManager
) : IPermissionChecker
{
    public async Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        return await IsGrantedAsync(principalAccessor.Principal, name, cancellationToken);
    }

    public async Task<bool> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(name);

        var permission = await permissionDefinitionManager.GetOrDefaultPermissionAsync(name, cancellationToken);

        if (permission is null)
        {
            return false;
        }

        if (!permission.IsEnabled)
        {
            return false;
        }

        if (!await StateCheckerManager.IsEnabledAsync(permission))
        {
            return false;
        }

        var multiTenancySide = claimsPrincipal?.GetMultiTenancySide() ?? currentTenant.GetMultiTenancySide();

        if (!permission.MultiTenancySide.HasFlag(multiTenancySide))
        {
            return false;
        }

        var isGranted = false;
        var context = new PermissionValueCheckContext(permission, claimsPrincipal);

        foreach (var provider in permissionValueProviderManager.ValueProviders)
        {
            if (context.Permission.Providers.Any() && !context.Permission.Providers.Contains(provider.Name))
            {
                continue;
            }

            var result = await provider.CheckAsync(context);

            if (result == PermissionGrantStatus.Granted)
            {
                isGranted = true;
            }
            else if (result is PermissionGrantStatus.Prohibited)
            {
                return false;
            }
        }

        return isGranted;
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        return await IsGrantedAsync(principalAccessor.Principal, names, cancellationToken);
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(names);

        var result = new MultiplePermissionGrantResult();

        if (names.Length == 0)
        {
            return result;
        }

        var multiTenancySide = claimsPrincipal?.GetMultiTenancySide() ?? currentTenant.GetMultiTenancySide();

        var permissionDefinitions = new List<PermissionDefinition>();

        foreach (var name in names)
        {
            var permission = await permissionDefinitionManager.GetOrDefaultPermissionAsync(name, cancellationToken);

            if (permission is null)
            {
                result.Result.Add(name, PermissionGrantStatus.Prohibited);

                continue;
            }

            result.Result.Add(name, PermissionGrantStatus.Undefined);

            if (
                permission.IsEnabled
                && await StateCheckerManager.IsEnabledAsync(permission)
                && permission.MultiTenancySide.HasFlag(multiTenancySide)
            )
            {
                permissionDefinitions.Add(permission);
            }
        }

        foreach (var provider in permissionValueProviderManager.ValueProviders)
        {
            var permissions = permissionDefinitions
                .Where(x => x.Providers.Count == 0 || x.Providers.Contains(provider.Name, StringComparer.Ordinal))
                .ToList();

            if (permissions.IsNullOrEmpty())
            {
                continue;
            }

            var context = new PermissionValuesCheckContext(permissions, claimsPrincipal);

            var multipleResult = await provider.CheckAsync(context);

            foreach (
                var grantResult in multipleResult.Result.Where(grantResult =>
                    result.Result.ContainsKey(grantResult.Key)
                    && result.Result[grantResult.Key].Status == PermissionGrantStatus.Undefined
                    && grantResult.Value != PermissionGrantStatus.Undefined
                )
            )
            {
                result.Result[grantResult.Key] = grantResult.Value;
                permissionDefinitions.RemoveAll(x => x.Name == grantResult.Key);
            }

            if (result.AllGranted || result.AllProhibited)
            {
                break;
            }
        }

        return result;
    }
}
