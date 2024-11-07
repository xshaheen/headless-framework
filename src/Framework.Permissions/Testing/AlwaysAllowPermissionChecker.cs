// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Permissions.Checkers;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.Testing;

/// <summary>
/// <para>Always allows for any permission.</para>
/// <para>
/// Use IServiceCollection.AddAlwaysAllowAuthorization() to replace
/// IPermissionChecker with this class. This is useful for tests.
/// </para>
/// </summary>
public sealed class AlwaysAllowPermissionChecker : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<bool> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string name,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(true);
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        return IsGrantedAsync(claimsPrincipal: null, names, cancellationToken);
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(
        ClaimsPrincipal? claimsPrincipal,
        string[] names,
        CancellationToken cancellationToken = default
    )
    {
        var result = new MultiplePermissionGrantResult(names, PermissionGrantStatus.Granted);

        return Task.FromResult(result);
    }
}
