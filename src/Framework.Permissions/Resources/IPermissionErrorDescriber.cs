// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Permissions.Resources;

public interface IPermissionErrorDescriber
{
    ValueTask<ErrorDescriptor> SomePermissionsAreNotDefined(IReadOnlyCollection<string> permissionNames);

    ValueTask<ErrorDescriptor> SomePermissionsAreDisabled(IReadOnlyCollection<string> permissionNames);

    ValueTask<ErrorDescriptor> ProviderNotDefinedForSomePermissions(
        IReadOnlyCollection<string> permissionNames,
        string providerName
    );

    ValueTask<ErrorDescriptor> PermissionIsNotDefined(string permissionName);

    ValueTask<ErrorDescriptor> PermissionDisabled(string permissionName);

    ValueTask<ErrorDescriptor> PermissionsProviderNotFound(string providerName);

    ValueTask<ErrorDescriptor> PermissionProviderNotDefined(string permissionName, string providerName);
}

public sealed class DefaultPermissionErrorDescriber : IPermissionErrorDescriber
{
    public ValueTask<ErrorDescriptor> SomePermissionsAreNotDefined(IReadOnlyCollection<string> permissionNames)
    {
        var error = new ErrorDescriptor("permissions:some-undefined", Messages.permissions_some_undefined).WithParam(
            "permissionNames",
            permissionNames
        );

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> SomePermissionsAreDisabled(IReadOnlyCollection<string> permissionNames)
    {
        var error = new ErrorDescriptor("permissions:some-disabled", Messages.permissions_some_disabled).WithParam(
            "permissionNames",
            permissionNames
        );

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> ProviderNotDefinedForSomePermissions(
        IReadOnlyCollection<string> permissionNames,
        string providerName
    )
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            Messages.permissions_provider_not_defined_for_some,
            providerName
        );

        var error = new ErrorDescriptor("permissions:provider-not-defined-for-some", description)
            .WithParam("permissionNames", permissionNames)
            .WithParam("providerName", providerName);

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> PermissionIsNotDefined(string permissionName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.permissions_undefined, permissionName);

        var error = new ErrorDescriptor("permissions:undefined", description).WithParam(
            "permissionName",
            permissionName
        );

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> PermissionDisabled(string permissionName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.permissions_disabled, permissionName);

        var error = new ErrorDescriptor("permissions:disabled", description).WithParam(
            "permissionName",
            permissionName
        );

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> PermissionsProviderNotFound(string providerName)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            Messages.permissions_provider_not_found,
            providerName
        );

        var error = new ErrorDescriptor("permissions:provider-not-found", description).WithParam(
            "providerName",
            providerName
        );

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> PermissionProviderNotDefined(string permissionName, string providerName)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            Messages.permissions_provider_not_defined,
            permissionName,
            providerName
        );

        var error = new ErrorDescriptor("permissions:provider-not-defined", description)
            .WithParam("permissionName", permissionName)
            .WithParam("providerName", providerName);

        return ValueTask.FromResult(error);
    }
}
