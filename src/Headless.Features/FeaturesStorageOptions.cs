// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting;

namespace Headless.Features;

/// <summary>Storage-layer configuration shared across all feature-management database providers.</summary>
[PublicAPI]
public sealed class FeaturesStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that contains the features tables. Default:
    /// <see cref="HeadlessStorageDefaults.Schema"/> (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Gets or sets the name of the table that stores per-provider feature values. Default: <see langword="null"/>,
    /// which uses the database's conventional name: <c>feature_values</c> on PostgreSQL, <c>FeatureValues</c>
    /// elsewhere. A configured name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? FeatureValuesTableName { get; set; }

    /// <summary>
    /// Gets or sets the name of the table that stores feature definitions. Default: <see langword="null"/>, which uses
    /// the database's conventional name: <c>feature_definitions</c> on PostgreSQL, <c>FeatureDefinitions</c>
    /// elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? FeatureDefinitionsTableName { get; set; }

    /// <summary>
    /// Gets or sets the name of the table that stores feature group definitions. Default: <see langword="null"/>,
    /// which uses the database's conventional name: <c>feature_group_definitions</c> on PostgreSQL,
    /// <c>FeatureGroupDefinitions</c> elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? FeatureGroupDefinitionsTableName { get; set; }

    /// <summary>
    /// Maximum length of the values table's <c>Name</c> column. Default: <see
    /// cref="FeatureValueRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    /// <remarks>
    /// Every <c>MaxLength</c> option sizes a column when the table is created, the EF Core model, and the lookup
    /// parameters of the raw providers. A table that already exists keeps its width until the application's own
    /// migration alters it, so change the option and the column together.
    /// </remarks>
    public int FeatureValuesNameMaxLength { get; set; } = FeatureValueRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the values table's <c>Value</c> column. Default: <see
    /// cref="FeatureValueRecordConstants.ValueMaxLength"/> (128).
    /// </summary>
    public int FeatureValuesValueMaxLength { get; set; } = FeatureValueRecordConstants.ValueMaxLength;

    /// <summary>
    /// Maximum length of the values table's <c>ProviderName</c> column. Default: <see
    /// cref="FeatureValueRecordConstants.ProviderNameMaxLength"/> (64).
    /// </summary>
    public int FeatureValuesProviderNameMaxLength { get; set; } = FeatureValueRecordConstants.ProviderNameMaxLength;

    /// <summary>
    /// Maximum length of the values table's <c>ProviderKey</c> column, the scope a value applies to. Default: <see
    /// cref="FeatureValueRecordConstants.ProviderKeyMaxLength"/> (64).
    /// </summary>
    public int FeatureValuesProviderKeyMaxLength { get; set; } = FeatureValueRecordConstants.ProviderKeyMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Name</c> and <c>ParentName</c> columns. Default: <see
    /// cref="FeatureDefinitionRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    public int FeatureDefinitionsNameMaxLength { get; set; } = FeatureDefinitionRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>DisplayName</c> column. Default: <see
    /// cref="FeatureDefinitionRecordConstants.DisplayNameMaxLength"/> (256).
    /// </summary>
    public int FeatureDefinitionsDisplayNameMaxLength { get; set; } =
        FeatureDefinitionRecordConstants.DisplayNameMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Description</c> column. Default: <see
    /// cref="FeatureDefinitionRecordConstants.DescriptionMaxLength"/> (256).
    /// </summary>
    public int FeatureDefinitionsDescriptionMaxLength { get; set; } =
        FeatureDefinitionRecordConstants.DescriptionMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>DefaultValue</c> column. Default: <see
    /// cref="FeatureDefinitionRecordConstants.DefaultValueMaxLength"/> (256).
    /// </summary>
    public int FeatureDefinitionsDefaultValueMaxLength { get; set; } =
        FeatureDefinitionRecordConstants.DefaultValueMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Providers</c> column, the comma-joined provider names. Default:
    /// <see cref="FeatureDefinitionRecordConstants.ProvidersMaxLength"/> (256).
    /// </summary>
    public int FeatureDefinitionsProvidersMaxLength { get; set; } = FeatureDefinitionRecordConstants.ProvidersMaxLength;

    /// <summary>
    /// Maximum length of a group name: the group definitions table's <c>Name</c> column and the definitions table's
    /// <c>GroupName</c> column. Default: <see cref="FeatureGroupDefinitionRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    public int FeatureGroupDefinitionsNameMaxLength { get; set; } = FeatureGroupDefinitionRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the group definitions table's <c>DisplayName</c> column. Default: <see
    /// cref="FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength"/> (256).
    /// </summary>
    public int FeatureGroupDefinitionsDisplayNameMaxLength { get; set; } =
        FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength;

    /// <summary>Returns the feature values table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveFeatureValuesTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(FeatureValuesTableName, style, "FeatureValues");
    }

    /// <summary>Returns the feature definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveFeatureDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(FeatureDefinitionsTableName, style, "FeatureDefinitions");
    }

    /// <summary>Returns the feature group definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveFeatureGroupDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(FeatureGroupDefinitionsTableName, style, "FeatureGroupDefinitions");
    }

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
        target.FeatureValuesNameMaxLength = FeatureValuesNameMaxLength;
        target.FeatureValuesValueMaxLength = FeatureValuesValueMaxLength;
        target.FeatureValuesProviderNameMaxLength = FeatureValuesProviderNameMaxLength;
        target.FeatureValuesProviderKeyMaxLength = FeatureValuesProviderKeyMaxLength;
        target.FeatureDefinitionsNameMaxLength = FeatureDefinitionsNameMaxLength;
        target.FeatureDefinitionsDisplayNameMaxLength = FeatureDefinitionsDisplayNameMaxLength;
        target.FeatureDefinitionsDescriptionMaxLength = FeatureDefinitionsDescriptionMaxLength;
        target.FeatureDefinitionsDefaultValueMaxLength = FeatureDefinitionsDefaultValueMaxLength;
        target.FeatureDefinitionsProvidersMaxLength = FeatureDefinitionsProvidersMaxLength;
        target.FeatureGroupDefinitionsNameMaxLength = FeatureGroupDefinitionsNameMaxLength;
        target.FeatureGroupDefinitionsDisplayNameMaxLength = FeatureGroupDefinitionsDisplayNameMaxLength;
    }
}

/// <summary>The column-length rules every features storage validator applies, relational and EF Core alike.</summary>
internal static class FeaturesStorageOptionsRules
{
    /// <summary>
    /// The widest sized <c>nvarchar</c> SQL Server accepts. Every provider applies it, so one configuration stays
    /// valid on each database.
    /// </summary>
    private const int _ColumnMaxLength = 4000;

    public static void AddColumnLengthRules(this AbstractValidator<FeaturesStorageOptions> validator)
    {
        validator.RuleFor(x => x.FeatureValuesNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureValuesValueMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureValuesProviderNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureValuesProviderKeyMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureDefinitionsNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureDefinitionsDisplayNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureDefinitionsDescriptionMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureDefinitionsDefaultValueMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureDefinitionsProvidersMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureGroupDefinitionsNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.FeatureGroupDefinitionsDisplayNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
    }
}
