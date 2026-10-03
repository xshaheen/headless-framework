// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Settings.Resources;

#pragma warning disable CA1863 // Use 'CompositeFormat'
/// <summary>Default implementation of <see cref="ISettingErrorsDescriptor"/> using built-in message resources.</summary>
public sealed class DefaultSettingErrorsDescriptor : ISettingErrorsDescriptor
{
    /// <inheritdoc/>
    public ErrorDescriptor NotDefined(string settingName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.setting_not_defined, settingName);
        var error = new ErrorDescriptor("setting:not_defined", description).WithParam("settingName", settingName);

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor ProviderNotFound(string providerName)
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

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor ProviderIsReadonly(string providerKey)
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

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor CurrentUserNotAvailable()
    {
        var error = new ErrorDescriptor("setting:user_not_available", Messages.setting_user_not_available);

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor CurrentTenantNotAvailable()
    {
        var error = new ErrorDescriptor("setting:tenant_not_available", Messages.setting_tenant_not_available);

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor DecryptionFailed(string settingName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.setting_decryption_failed, settingName);

        var error = new ErrorDescriptor("setting:decryption_failed", description).WithParam("settingName", settingName);

        return error;
    }
}
