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

    /// <summary>
    /// Returns the error for a versioned write whose grants changed after the caller read their version.
    /// </summary>
    /// <param name="providerName">The grant provider name the write targeted.</param>
    /// <returns>An error with the code <c>permissions:version-mismatch</c>.</returns>
    /// <remarks>A default member, so an existing implementation keeps compiling and returns the built-in message.</remarks>
    ErrorDescriptor VersionMismatch(string providerName) =>
        DefaultPermissionErrorsDescriptor.CreateVersionMismatch(providerName);
}

#pragma warning disable CA1863 // Use 'CompositeFormat'
public sealed class DefaultPermissionErrorsDescriptor : IPermissionErrorsDescriptor
{
    public ErrorDescriptor SomePermissionsAreNotDefined(IReadOnlyCollection<string> permissionNames)
    {
        var error = new ErrorDescriptor("permissions:some-undefined", Messages.permissions_some_undefined).WithParam(
            "permissionNames",
            permissionNames
        );

        return error;
    }

    public ErrorDescriptor SomePermissionsAreDisabled(IReadOnlyCollection<string> permissionNames)
    {
        var error = new ErrorDescriptor("permissions:some-disabled", Messages.permissions_some_disabled).WithParam(
            "permissionNames",
            permissionNames
        );

        return error;
    }

    public ErrorDescriptor ProviderNotDefinedForSomePermissions(
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

        return error;
    }

    public ErrorDescriptor PermissionIsNotDefined(string permissionName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.permissions_undefined, permissionName);

        var error = new ErrorDescriptor("permissions:undefined", description).WithParam(
            "permissionName",
            permissionName
        );

        return error;
    }

    public ErrorDescriptor PermissionDisabled(string permissionName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.permissions_disabled, permissionName);

        var error = new ErrorDescriptor("permissions:disabled", description).WithParam(
            "permissionName",
            permissionName
        );

        return error;
    }

    public ErrorDescriptor PermissionsProviderNotFound(string providerName)
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

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor VersionMismatch(string providerName) => CreateVersionMismatch(providerName);

    internal static ErrorDescriptor CreateVersionMismatch(string providerName)
    {
        return new ErrorDescriptor("permissions:version-mismatch", Messages.permissions_version_mismatch).WithParam(
            "providerName",
            providerName
        );
    }

    public ErrorDescriptor PermissionProviderNotDefined(string permissionName, string providerName)
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

        return error;
    }
}
