// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Features.Storage.EntityFramework;

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
        RuleFor(x => x.Schema).NotEmpty();
        RuleFor(x => x.FeatureValuesTableName).NotEmpty();
        RuleFor(x => x.FeatureDefinitionsTableName).NotEmpty();
        RuleFor(x => x.FeatureGroupDefinitionsTableName).NotEmpty();
    }
}
