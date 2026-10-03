// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Settings;

/// <summary>Validates <see cref="SettingsStorageOptions"/> for a relational provider's identifier rules.</summary>
internal sealed class RelationalSettingsStorageOptionsValidator : AbstractValidator<SettingsStorageOptions>
{
    public RelationalSettingsStorageOptionsValidator(StorageProvider provider)
    {
        // Every provider measures derived names against PostgreSQL's limit, so a table name valid on one database
        // stays valid on the others.
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.SettingValuesTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(SettingsStorageNames.ValuesIndexes)
            .When(x => x.SettingValuesTableName is not null);
        RuleFor(x => x.SettingDefinitionsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(SettingsStorageNames.DefinitionsIndexes)
            .When(x => x.SettingDefinitionsTableName is not null);
    }
}
