// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Constants;

namespace Headless.Features;

/// <summary>Validates <see cref="FeaturesStorageOptions"/> for a relational provider's identifier rules.</summary>
internal sealed class RelationalFeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
{
    public RelationalFeaturesStorageOptionsValidator(StorageProvider provider)
    {
        // Every provider measures derived names against PostgreSQL's limit, so a table name valid on one database
        // stays valid on the others.
        RuleFor(x => x.Schema).IsValidIdentifierFor(provider);
        RuleFor(x => x.FeatureValuesTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(FeaturesStorageNames.ValuesIndexes)
            .When(x => x.FeatureValuesTableName is not null);
        RuleFor(x => x.FeatureDefinitionsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(FeaturesStorageNames.DefinitionsIndexes)
            .When(x => x.FeatureDefinitionsTableName is not null);
        RuleFor(x => x.FeatureGroupDefinitionsTableName)
            .IsValidIdentifierFor(provider)
            .FitsDerivedPostgreSqlNames(FeaturesStorageNames.GroupsIndexes)
            .When(x => x.FeatureGroupDefinitionsTableName is not null);
    }
}
