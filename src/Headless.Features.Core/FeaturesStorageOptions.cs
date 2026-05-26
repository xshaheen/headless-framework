// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Storage;

namespace Headless.Features;

[PublicAPI]
public sealed class FeaturesStorageOptions
{
    public string Schema { get; set; } = "features";

    public string FeatureValuesTableName { get; set; } = "FeatureValues";

    public string FeatureDefinitionsTableName { get; set; } = "FeatureDefinitions";

    public string FeatureGroupDefinitionsTableName { get; set; } = "FeatureGroupDefinitions";

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(FeaturesStorageOptions target)
    {
        target.Schema = Schema;
        target.FeatureValuesTableName = FeatureValuesTableName;
        target.FeatureDefinitionsTableName = FeatureDefinitionsTableName;
        target.FeatureGroupDefinitionsTableName = FeatureGroupDefinitionsTableName;
    }
}

internal sealed class FeaturesStorageOptionsValidator : AbstractValidator<FeaturesStorageOptions>
{
    public FeaturesStorageOptionsValidator()
    {
        // Cap at SqlServer's regular-identifier max (128). Shorter PG limits (63) are enforced by
        // the PG initializer's DDL at startup rather than by this shared validator, so SqlServer-
        // only consumers can use schema/table names PG wouldn't accept.
        RuleFor(x => x.Schema).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        RuleFor(x => x.FeatureValuesTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        RuleFor(x => x.FeatureDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
        RuleFor(x => x.FeatureGroupDefinitionsTableName).NotEmpty().Matches(StorageIdentifier.PgPattern).MaximumLength(StorageIdentifier.SqlServerMaxLength);
    }
}
