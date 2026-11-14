// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Hosting;

namespace Headless.Permissions;

/// <summary>
/// Shared storage configuration for the Headless Permissions system. Applied to whichever storage
/// provider is registered (EF Core, PostgreSQL raw-DDL, or SQL Server raw-DDL).
/// </summary>
[PublicAPI]
public sealed class PermissionsStorageOptions
{
    /// <summary>
    /// Database schema that contains all permissions tables. Defaults to
    /// <see cref="HeadlessStorageDefaults.Schema"/> (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Table name for permission grant records. Default: <see langword="null"/>, which uses the database's
    /// conventional name: <c>permission_grants</c> on PostgreSQL, <c>PermissionGrants</c> elsewhere. A configured
    /// name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? PermissionGrantsTableName { get; set; }

    /// <summary>
    /// Table name for static permission definition records. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>permission_definitions</c> on PostgreSQL, <c>PermissionDefinitions</c>
    /// elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? PermissionDefinitionsTableName { get; set; }

    /// <summary>
    /// Table name for permission group definition records. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>permission_group_definitions</c> on PostgreSQL,
    /// <c>PermissionGroupDefinitions</c> elsewhere. A configured name is used verbatim.
    /// </summary>
    public string? PermissionGroupDefinitionsTableName { get; set; }

    /// <summary>
    /// Maximum length of the grants table's <c>Name</c> column. Default:
    /// <see cref="PermissionGrantRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    /// <remarks>
    /// Every <c>MaxLength</c> option sizes a column when the table is created, the EF Core model, and the lookup
    /// parameters of the raw providers. A table that already exists keeps its width until the application's own
    /// migration alters it, so change the option and the column together.
    /// </remarks>
    public int PermissionGrantsNameMaxLength { get; set; } = PermissionGrantRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the grants table's <c>ProviderName</c> column. Default:
    /// <see cref="PermissionGrantRecordConstants.ProviderNameMaxLength"/> (64).
    /// </summary>
    public int PermissionGrantsProviderNameMaxLength { get; set; } =
        PermissionGrantRecordConstants.ProviderNameMaxLength;

    /// <summary>
    /// Maximum length of the grants table's <c>ProviderKey</c> column, the subject a grant applies to. Default:
    /// <see cref="PermissionGrantRecordConstants.ProviderKeyMaxLength"/> (64). Raise it when the application's
    /// provider keys are longer, such as a key composed of two identifiers.
    /// </summary>
    public int PermissionGrantsProviderKeyMaxLength { get; set; } = PermissionGrantRecordConstants.ProviderKeyMaxLength;

    /// <summary>
    /// Maximum length of the grants table's <c>TenantId</c> column. Default:
    /// <see cref="PermissionGrantRecordConstants.TenantIdMaxLength"/> (41).
    /// </summary>
    public int PermissionGrantsTenantIdMaxLength { get; set; } = PermissionGrantRecordConstants.TenantIdMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Name</c> and <c>ParentName</c> columns. Default:
    /// <see cref="PermissionDefinitionRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    public int PermissionDefinitionsNameMaxLength { get; set; } = PermissionDefinitionRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>DisplayName</c> column. Default:
    /// <see cref="PermissionDefinitionRecordConstants.DisplayNameMaxLength"/> (256).
    /// </summary>
    public int PermissionDefinitionsDisplayNameMaxLength { get; set; } =
        PermissionDefinitionRecordConstants.DisplayNameMaxLength;

    /// <summary>
    /// Maximum length of the definitions table's <c>Providers</c> column, the comma-joined provider names. Default:
    /// <see cref="PermissionDefinitionRecordConstants.ProvidersMaxLength"/> (128).
    /// </summary>
    public int PermissionDefinitionsProvidersMaxLength { get; set; } =
        PermissionDefinitionRecordConstants.ProvidersMaxLength;

    /// <summary>
    /// Maximum length of a group name: the group definitions table's <c>Name</c> column and the definitions table's
    /// <c>GroupName</c> column. Default: <see cref="PermissionGroupDefinitionRecordConstants.NameMaxLength"/> (128).
    /// </summary>
    public int PermissionGroupDefinitionsNameMaxLength { get; set; } =
        PermissionGroupDefinitionRecordConstants.NameMaxLength;

    /// <summary>
    /// Maximum length of the group definitions table's <c>DisplayName</c> column. Default:
    /// <see cref="PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength"/> (256).
    /// </summary>
    public int PermissionGroupDefinitionsDisplayNameMaxLength { get; set; } =
        PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength;

    /// <summary>Returns the permission grants table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolvePermissionGrantsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(PermissionGrantsTableName, style, "PermissionGrants");
    }

    /// <summary>Returns the permission definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolvePermissionDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(PermissionDefinitionsTableName, style, "PermissionDefinitions");
    }

    /// <summary>Returns the permission group definitions table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolvePermissionGroupDefinitionsTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(PermissionGroupDefinitionsTableName, style, "PermissionGroupDefinitions");
    }

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(PermissionsStorageOptions target)
    {
        target.Schema = Schema;
        target.PermissionGrantsTableName = PermissionGrantsTableName;
        target.PermissionDefinitionsTableName = PermissionDefinitionsTableName;
        target.PermissionGroupDefinitionsTableName = PermissionGroupDefinitionsTableName;
        target.PermissionGrantsNameMaxLength = PermissionGrantsNameMaxLength;
        target.PermissionGrantsProviderNameMaxLength = PermissionGrantsProviderNameMaxLength;
        target.PermissionGrantsProviderKeyMaxLength = PermissionGrantsProviderKeyMaxLength;
        target.PermissionGrantsTenantIdMaxLength = PermissionGrantsTenantIdMaxLength;
        target.PermissionDefinitionsNameMaxLength = PermissionDefinitionsNameMaxLength;
        target.PermissionDefinitionsDisplayNameMaxLength = PermissionDefinitionsDisplayNameMaxLength;
        target.PermissionDefinitionsProvidersMaxLength = PermissionDefinitionsProvidersMaxLength;
        target.PermissionGroupDefinitionsNameMaxLength = PermissionGroupDefinitionsNameMaxLength;
        target.PermissionGroupDefinitionsDisplayNameMaxLength = PermissionGroupDefinitionsDisplayNameMaxLength;
    }
}

/// <summary>The column-length rules every permissions storage validator applies, relational and EF Core alike.</summary>
internal static class PermissionsStorageOptionsRules
{
    /// <summary>
    /// The widest sized <c>nvarchar</c> SQL Server accepts. Every provider applies it, so one configuration stays
    /// valid on each database.
    /// </summary>
    private const int _ColumnMaxLength = 4000;

    public static void AddColumnLengthRules(this AbstractValidator<PermissionsStorageOptions> validator)
    {
        validator.RuleFor(x => x.PermissionGrantsNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionGrantsProviderNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionGrantsProviderKeyMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionGrantsTenantIdMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionDefinitionsNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionDefinitionsDisplayNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionDefinitionsProvidersMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionGroupDefinitionsNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
        validator.RuleFor(x => x.PermissionGroupDefinitionsDisplayNameMaxLength).InclusiveBetween(1, _ColumnMaxLength);
    }
}
