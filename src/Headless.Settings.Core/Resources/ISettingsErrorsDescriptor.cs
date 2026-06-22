// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Settings.Resources;

/// <summary>Describes localised error descriptors for settings management failures.</summary>
public interface ISettingsErrorsDescriptor
{
    /// <summary>Returns an error descriptor for a setting that is not defined.</summary>
    /// <param name="settingName">The name of the undefined setting.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ValueTask<ErrorDescriptor> NotDefined(string settingName);

    /// <summary>Returns an error descriptor when a requested setting value provider is not registered.</summary>
    /// <param name="providerName">The name of the missing provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ValueTask<ErrorDescriptor> ProviderNotFound(string providerName);

    /// <summary>Returns an error descriptor when a setting value provider does not support write operations.</summary>
    /// <param name="providerKey">The key identifying the read-only provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ValueTask<ErrorDescriptor> ProviderIsReadonly(string providerKey);

    /// <summary>Returns an error descriptor when the current user is not available in the current scope.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ValueTask<ErrorDescriptor> CurrentUserNotAvailable();

    /// <summary>Returns an error descriptor when the current tenant is not available in the current scope.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ValueTask<ErrorDescriptor> CurrentTenantNotAvailable();
}

#pragma warning disable CA1863 // Use 'CompositeFormat'
/// <summary>Default implementation of <see cref="ISettingsErrorsDescriptor"/> using built-in message resources.</summary>
public sealed class DefaultSettingsErrorsDescriptor : ISettingsErrorsDescriptor
{
    /// <inheritdoc/>
    public ValueTask<ErrorDescriptor> NotDefined(string settingName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.setting_not_defined, settingName);
        var error = new ErrorDescriptor("setting:not_defined", description).WithParam("settingName", settingName);

        return ValueTask.FromResult(error);
    }

    /// <inheritdoc/>
    public ValueTask<ErrorDescriptor> ProviderNotFound(string providerName)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            Messages.setting_provider_not_found,
            providerName
        );

        var error = new ErrorDescriptor("setting:provider_not_found", description).WithParam(
            "providerName",
            providerName
        );

        return ValueTask.FromResult(error);
    }

    /// <inheritdoc/>
    public ValueTask<ErrorDescriptor> ProviderIsReadonly(string providerKey)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            Messages.setting_provider_is_readonly,
            providerKey
        );

        var error = new ErrorDescriptor("setting:provider_is_readonly", description).WithParam(
            "providerKey",
            providerKey
        );

        return ValueTask.FromResult(error);
    }

    /// <inheritdoc/>
    public ValueTask<ErrorDescriptor> CurrentUserNotAvailable()
    {
        var error = new ErrorDescriptor("setting:user_not_available", Messages.setting_user_not_available);

        return ValueTask.FromResult(error);
    }

    /// <inheritdoc/>
    public ValueTask<ErrorDescriptor> CurrentTenantNotAvailable()
    {
        var error = new ErrorDescriptor("setting:tenant_not_available", Messages.setting_tenant_not_available);

        return ValueTask.FromResult(error);
    }
}
