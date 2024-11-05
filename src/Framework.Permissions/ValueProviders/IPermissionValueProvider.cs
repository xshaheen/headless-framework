// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

public interface IPermissionValueProvider
{
    string Name { get; }

    Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context);

    Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context);
}

public abstract class StorePermissionValueProvider(IPermissionStore store) : IPermissionValueProvider
{
    public abstract string Name { get; }

    protected IPermissionStore PermissionStore { get; } = store;

    public abstract Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context);

    public abstract Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context);
}
