// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Permissions;

/// <summary>
/// The PascalCase parts, after the table name, of every index the permissions tables carry. The EF mapping builds its
/// index names from them, and the option validators measure the names they derive from a configured table name.
/// </summary>
internal static class PermissionsStorageNames
{
    public static readonly string[] GrantsByTenant = ["TenantId", "Name", "ProviderName", "ProviderKey"];

    // "NoTenant" rather than "NullTenantId": with the default table the snake_case form of the longer suffix is
    // 67 bytes, past PostgreSQL's 63-byte identifier limit, which would silently truncate it.
    public static readonly string[] GrantsByNoTenant = ["Name", "ProviderName", "ProviderKey", "NoTenant"];

    public static readonly string[] DefinitionsByName = ["Name"];

    public static readonly string[] DefinitionsByGroupName = ["GroupName"];

    public static readonly string[] GroupsByName = ["Name"];

    public static readonly string[][] GrantsIndexes = [GrantsByTenant, GrantsByNoTenant];

    public static readonly string[][] DefinitionsIndexes = [DefinitionsByName, DefinitionsByGroupName];

    public static readonly string[][] GroupsIndexes = [GroupsByName];
}
