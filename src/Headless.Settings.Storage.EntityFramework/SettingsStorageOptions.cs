// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting.Storage;

namespace Headless.Settings;

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
        RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.SettingValuesTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.SettingDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
    }
}
