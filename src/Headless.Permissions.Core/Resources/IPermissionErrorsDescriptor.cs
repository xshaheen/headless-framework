// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Permissions.Resources;

/// <summary>
/// Produces localized <see cref="ErrorDescriptor"/> instances for permission-related conflict errors.
/// Replace the default implementation via DI to customize error codes or messages.
/// </summary>
public interface IPermissionErrorsDescriptor
{
    ErrorDescriptor SomePermissionsAreNotDefined(IReadOnlyCollection<string> permissionNames);

    ErrorDescriptor SomePermissionsAreDisabled(IReadOnlyCollection<string> permissionNames);

    ErrorDescriptor ProviderNotDefinedForSomePermissions(
        IReadOnlyCollection<string> permissionNames,
        string providerName
    );

    ErrorDescriptor PermissionIsNotDefined(string permissionName);

    ErrorDescriptor PermissionDisabled(string permissionName);

    ErrorDescriptor PermissionsProviderNotFound(string providerName);

    ErrorDescriptor PermissionProviderNotDefined(string permissionName, string providerName);
}
