// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Permissions.Definitions;
using Headless.Permissions.GrantProviders;

namespace Headless.Permissions.Models;

public sealed class PermissionManagementProvidersOptions
{
    public TypeList<IPermissionDefinitionProvider> DefinitionProviders { get; } = [];

    public TypeList<IPermissionGrantProvider> GrantProviders { get; } = [];

    public HashSet<string> DeletedPermissionGroups { get; } = [];

    public HashSet<string> DeletedPermissions { get; } = [];
}
