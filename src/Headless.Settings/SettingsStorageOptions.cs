// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting;

namespace Headless.Settings;

/// <summary>Configuration options for the settings storage layer (schema name, table names, and startup behaviour).</summary>
[PublicAPI]
public sealed class SettingsStorageOptions
{
    /// <summary>
    /// Gets or sets the database schema that contains the settings tables. Defaults to
    /// <see cref="HeadlessStorageDefaults.Schema"/> (<c>headless</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Gets or sets the name of the table that stores setting values. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>setting_values</c> on PostgreSQL, <c>SettingValues</c> elsewhere. A configured
    /// name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? SettingValuesTableName { get; set; }

    /// <summary>
    /// Gets or sets the name of the table that stores setting definitions. Default: <see langword="null"/>, which uses
    /// the database's conventional name: <c>setting_definitions</c> on PostgreSQL, <c>SettingDefinitions</c> elsewhere.
    /// A configured name is used verbatim.
    /// </summary>
    public string? SettingDefinitionsTableName { get; set; }

    /// <summary>
    /// Maximum length of the values table's <c>Name</c> column. Default: <see
    /// cref="SettingValueRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    /// <remarks>
    /// Every <c>MaxLength</c> option sizes a column when the table is created, the EF Core model, and the lookup
    /// parameters of the raw providers. A table that already exists keeps its width until the application's own
    /// migration alters it, so change the option and the column together.
    /// </remarks>
    public int SettingValuesNameMaxLength { get; set; } = SettingValueRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the values table's <c>Value</c> column. Default: <see
    /// cref="SettingValueRecordConstants.ValueMaxLength"/> (2000).
    /// </summary>
    public int SettingValuesValueMaxLength { get; set; } = SettingValueRecordConstants.ValueMaxLength;

    /// <summary>
    /// Maximum length of the values table's <c>ProviderName</c> column. Default: <see
    /// cref="SettingValueRecordConstants.ProviderNameMaxLength"/> (64).
    /// </summary>
    public int SettingValuesProviderNameMaxLength { get; set; } = SettingValueRecordConstants.ProviderNameMaxLength;

    /// <summary>
    /// Maximum length of the values table's <c>ProviderKey</c> column, the scope a value applies to. Default: <see
    /// cref="SettingValueRecordConstants.ProviderKeyMaxLength"/> (64).
    /// </summary>
    public int SettingValuesProviderKeyMaxLength { get; set; } = SettingValueRecordConstants.ProviderKeyMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Name</c> column. Default: <see
    /// cref="SettingDefinitionRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    public int SettingDefinitionsNameMaxLength { get; set; } = SettingDefinitionRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>DisplayName</c> column. Default: <see
    /// cref="SettingDefinitionRecordConstants.DisplayNameMaxLength"/> (256).
    /// </summary>
    public int SettingDefinitionsDisplayNameMaxLength { get; set; } =
        SettingDefinitionRecordConstants.DisplayNameMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Description</c> column. Default: <see
    /// cref="SettingDefinitionRecordConstants.DescriptionMaxLength"/> (512).
    /// </summary>
    public int SettingDefinitionsDescriptionMaxLength { get; set; } =
        SettingDefinitionRecordConstants.DescriptionMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>DefaultValue</c> column. It is independent of <see
    /// cref="SettingValuesValueMaxLength"/>: raise both when stored values and defaults grow together. Default:
    /// <see cref="SettingDefinitionRecordConstants.DefaultValueMaxLength"/> (2000).
    /// </summary>
    public int SettingDefinitionsDefaultValueMaxLength { get; set; } =
        SettingDefinitionRecordConstants.DefaultValueMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Providers</c> column, the comma-joined provider names. Default:
    /// <see cref="SettingDefinitionRecordConstants.ProvidersMaxLength"/> (1024).
    /// </summary>
    public int SettingDefinitionsProvidersMaxLength { get; set; } = SettingDefinitionRecordConstants.ProvidersMaxLength;

    /// <summary>Returns the setting values table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveSettingValuesTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(SettingValuesTableName, style, "SettingValues");
    }

    /// <summary>Returns the setting definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveSettingDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(SettingDefinitionsTableName, style, "SettingDefinitions");
    }

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(SettingsStorageOptions target)
    {
        target.Schema = Schema;
        target.SettingValuesTableName = SettingValuesTableName;
        target.SettingDefinitionsTableName = SettingDefinitionsTableName;
        target.SettingValuesNameMaxLength = SettingValuesNameMaxLength;
        target.SettingValuesValueMaxLength = SettingValuesValueMaxLength;
        target.SettingValuesProviderNameMaxLength = SettingValuesProviderNameMaxLength;
        target.SettingValuesProviderKeyMaxLength = SettingValuesProviderKeyMaxLength;
        target.SettingDefinitionsNameMaxLength = SettingDefinitionsNameMaxLength;
        target.SettingDefinitionsDisplayNameMaxLength = SettingDefinitionsDisplayNameMaxLength;
        target.SettingDefinitionsDescriptionMaxLength = SettingDefinitionsDescriptionMaxLength;
        target.SettingDefinitionsDefaultValueMaxLength = SettingDefinitionsDefaultValueMaxLength;
        target.SettingDefinitionsProvidersMaxLength = SettingDefinitionsProvidersMaxLength;
    }
}

/// <summary>The column-length rules every settings storage validator applies, relational and EF Core alike.</summary>
internal static class SettingsStorageOptionsRules
{
    /// <summary>
    /// The widest sized <c>nvarchar</c> SQL Server accepts. Every provider applies it, so one configuration stays
    /// valid on each database.
    /// </summary>
    private const int _ColumnMaxLength = 4000;

    public static void AddColumnLengthRules(this AbstractValidator<SettingsStorageOptions> validator)
    {
        validator.RuleFor(x => x.SettingValuesNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingValuesValueMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingValuesProviderNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingValuesProviderKeyMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingDefinitionsNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingDefinitionsDisplayNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingDefinitionsDescriptionMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingDefinitionsDefaultValueMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.SettingDefinitionsProvidersMaxLength).InclusiveBetween(1, _ColumnMaxLength);
    }
}
