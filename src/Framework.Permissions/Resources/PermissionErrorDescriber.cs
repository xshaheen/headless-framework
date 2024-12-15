// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Permissions.Resources;

public static class PermissionErrorDescriber
{
    public static ErrorDescriptor SomePermissionsAreNotDefined(IReadOnlyCollection<string> permissionNames)
    {
        return new ErrorDescriptor("permissions:some-undefined", "Some permissions are not defined.").WithParam(
            "permissionNames",
            permissionNames
        );
    }

    public static ErrorDescriptor SomePermissionsAreDisabled(IReadOnlyCollection<string> permissionNames)
    {
        return new ErrorDescriptor("permissions:some-disabled", "Some permissions are disabled.").WithParam(
            "permissionNames",
            permissionNames
        );
    }

    public static ErrorDescriptor ProviderNotDefinedForSomePermissions(
        IReadOnlyCollection<string> permissionNames,
        string providerName
    )
    {
        return new ErrorDescriptor(
            "permissions:provider-not-defined-for-some",
            $"The provider named '{providerName}' is not defined for some permissions."
        )
            .WithParam("permissionNames", permissionNames)
            .WithParam("providerName", providerName);
    }

    public static ErrorDescriptor PermissionIsNotDefined(string permissionName)
    {
        return new ErrorDescriptor(
            "permissions:undefined",
            $"The permission named '{permissionName}' is undefined."
        ).WithParam("permissionName", permissionName);
    }

    public static ErrorDescriptor PermissionDisabled(string permissionName)
    {
        return new ErrorDescriptor(
            "permissions:disabled",
            $"The permission named '{permissionName}' is disabled."
        ).WithParam("permissionName", permissionName);
    }

    public static ErrorDescriptor PermissionsProviderNotFound(string providerName)
    {
        return new ErrorDescriptor(
            "permissions:provider-not-found",
            $"Unknown permission management provider: {providerName}"
        ).WithParam("providerName", providerName);
    }

    public static ErrorDescriptor PermissionProviderNotDefined(string permissionName, string providerName)
    {
        return new ErrorDescriptor(
            "permissions:provider-not-defined",
            $"The permission named '{permissionName}' does not have a provider named '{providerName}'."
        )
            .WithParam("permissionName", permissionName)
            .WithParam("providerName", providerName);
    }
}
