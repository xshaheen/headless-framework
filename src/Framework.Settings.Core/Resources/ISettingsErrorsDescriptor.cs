// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Settings.Resources;

public interface ISettingsErrorsDescriptor
{
    ValueTask<ErrorDescriptor> NotDefined(string settingName);

    ValueTask<ErrorDescriptor> ProviderNotFound(string providerName);

    ValueTask<ErrorDescriptor> ProviderIsReadonly(string providerKey);

    ValueTask<ErrorDescriptor> CurrentUserNotAvailable();

    ValueTask<ErrorDescriptor> CurrentTenantNotAvailable();
}

public sealed class DefaultSettingsErrorsDescriptor : ISettingsErrorsDescriptor
{
    public ValueTask<ErrorDescriptor> NotDefined(string settingName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, Messages.setting_not_defined, settingName);
        var error = new ErrorDescriptor("setting:not_defined", description).WithParam("settingName", settingName);

        return ValueTask.FromResult(error);
    }

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

    public ValueTask<ErrorDescriptor> CurrentUserNotAvailable()
    {
        var error = new ErrorDescriptor("setting:user_not_available", Messages.setting_user_not_available);

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> CurrentTenantNotAvailable()
    {
        var error = new ErrorDescriptor("setting:tenant_not_available", Messages.setting_tenant_not_available);

        return ValueTask.FromResult(error);
    }
}
