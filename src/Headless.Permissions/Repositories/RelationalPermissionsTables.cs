// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;

namespace Headless.Permissions;

/// <summary>
/// The permissions tables' names in one dialect's convention, shared by the relational repositories and each
/// provider's schema contribution so the DDL and the statements name the same objects.
/// </summary>
internal sealed class RelationalPermissionsTables
{
    public RelationalPermissionsTables(ISqlDialect dialect, PermissionsStorageOptions options)
    {
        Dialect = dialect;
        Schema = options.Schema;
        // A configured name is used verbatim; the default follows the dialect's convention.
        GrantsName = options.PermissionGrantsTableName ?? dialect.Name("PermissionGrants");
        DefinitionsName = options.PermissionDefinitionsTableName ?? dialect.Name("PermissionDefinitions");
        GroupsName = options.PermissionGroupDefinitionsTableName ?? dialect.Name("PermissionGroupDefinitions");
        Grants = dialect.Qualify(Schema, GrantsName);
        Definitions = dialect.Qualify(Schema, DefinitionsName);
        Groups = dialect.Qualify(Schema, GroupsName);
    }

    public ISqlDialect Dialect { get; }

    public string Schema { get; }

    /// <summary>The unqualified, unquoted grant table name.</summary>
    public string GrantsName { get; }

    /// <summary>The unqualified, unquoted permission definition table name.</summary>
    public string DefinitionsName { get; }

    /// <summary>The unqualified, unquoted group definition table name.</summary>
    public string GroupsName { get; }

    /// <summary>The quoted, schema-qualified grant table.</summary>
    public string Grants { get; }

    /// <summary>The quoted, schema-qualified permission definition table.</summary>
    public string Definitions { get; }

    /// <summary>The quoted, schema-qualified group definition table.</summary>
    public string Groups { get; }

    /// <summary>Returns a column, in the dialect's convention and quoted.</summary>
    public string Column(string pascalName) => Dialect.Quote(Dialect.Name(pascalName));
}
