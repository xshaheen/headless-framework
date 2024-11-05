// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Framework.Permissions.Values;

public interface IPermissionStore
{
    Task<bool> IsGrantedAsync(string name, string providerName, string? providerKey);

    Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names, string providerName, string? providerKey);
}

public sealed class NullPermissionStore : IPermissionStore
{
    public ILogger<NullPermissionStore> Logger { get; set; } = NullLogger<NullPermissionStore>.Instance;

    public Task<bool> IsGrantedAsync(string name, string providerName, string? providerKey)
    {
        return Task.FromResult(false);
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] names, string providerName, string? providerKey)
    {
        return Task.FromResult(new MultiplePermissionGrantResult(names, PermissionGrantResult.Prohibited));
    }
}
