// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.Checkers;

[PublicAPI]
public interface IPermissionChecker
{
    Task<bool> IsGrantedAsync(string name);

    Task<bool> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string name);

    Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names);

    Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal? claimsPrincipal, string[] names);
}
