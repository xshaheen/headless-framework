// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Definitions;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.Checkers;

[PublicAPI]
public interface IPermissionChecker
{
    Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default);

    Task<MultiplePermissionGrantResult> IsGrantedAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default
    );

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
    IPermissionGrantProviderManager permissionValueProviderManager
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

        var permission = await permissionDefinitionManager.GetOrDefaultAsync(name, cancellationToken);

        if (permission is null)
        {
            return false;
        }

        if (!permission.IsEnabled)
        {
            return false;
        }

        var isGranted = false;
        var context = new PermissionValueCheckContext();

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
        IReadOnlyCollection<string> names,
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

        var permissionDefinitions = new List<PermissionDefinition>();

        foreach (var name in names)
        {
            var permission = await permissionDefinitionManager.GetOrDefaultAsync(name, cancellationToken);

            if (permission is null)
            {
                result.Add(name, PermissionGrantStatus.Prohibited);

                continue;
            }

            result.Add(name, PermissionGrantStatus.Undefined);

            if (permission.IsEnabled)
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
                    result.ContainsKey(grantResult.Key)
                    && result[grantResult.Key].Status == PermissionGrantStatus.Undefined
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
