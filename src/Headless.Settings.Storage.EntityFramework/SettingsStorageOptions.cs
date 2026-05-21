// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Settings.Storage.EntityFramework;

[PublicAPI]
public sealed class SettingsStorageOptions
{
    public string Schema { get; set; } = "settings";

    public string SettingValuesTableName { get; set; } = "SettingValues";

    public string SettingDefinitionsTableName { get; set; } = "SettingDefinitions";
}

internal sealed class SettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
{
    public SettingsStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).NotEmpty();
        RuleFor(x => x.SettingValuesTableName).NotEmpty();
        RuleFor(x => x.SettingDefinitionsTableName).NotEmpty();
    }
}
