// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting.Storage;

namespace Headless.Features;

[PublicAPI]
public sealed class FeaturesStorageOptions
{
    public string Schema { get; set; } = "features";

    public string FeatureValuesTableName { get; set; } = "FeatureValues";

    public string FeatureDefinitionsTableName { get; set; } = "FeatureDefinitions";

    public string FeatureGroupDefinitionsTableName { get; set; } = "FeatureGroupDefinitions";
}

internal sealed class FeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
{
    public FeaturesStorageOptionsValidator()
    {
        RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.FeatureValuesTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.FeatureDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
        RuleFor(x => x.FeatureGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.PgMaxLength);
    }
}
