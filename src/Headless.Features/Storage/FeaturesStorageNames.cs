// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// The PascalCase parts, after the table name, of every index the features tables carry. The EF mapping builds its
/// index names from them, and the option validators measure the names they derive from a configured table name.
/// </summary>
internal static class FeaturesStorageNames
{
    public static readonly string[] ValuesByNameProviderKey = ["Name", "ProviderName", "ProviderKey"];

    public static readonly string[] ValuesByNameNullProviderKey = ["Name", "ProviderName", "NullProviderKey"];

    public static readonly string[] ValuesByProvider = ["ProviderName", "ProviderKey"];

    public static readonly string[] DefinitionsByName = ["Name"];

    public static readonly string[] DefinitionsByGroupName = ["GroupName"];

    public static readonly string[] GroupsByName = ["Name"];

    public static readonly string[][] ValuesIndexes =
    [
        ValuesByNameProviderKey,
        ValuesByNameNullProviderKey,
        ValuesByProvider,
    ];

    public static readonly string[][] DefinitionsIndexes = [DefinitionsByName, DefinitionsByGroupName];

    public static readonly string[][] GroupsIndexes = [GroupsByName];
}
