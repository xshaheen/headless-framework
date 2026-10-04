// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings;

/// <summary>
/// The PascalCase parts, after the table name, of every index the settings tables carry. The EF mapping builds its
/// index names from them, and the option validators measure the names they derive from a configured table name.
/// </summary>
internal static class SettingsStorageNames
{
    public static readonly string[] ValuesByNameProviderKey = ["Name", "ProviderName", "ProviderKey"];

    public static readonly string[] ValuesByNameNullProviderKey = ["Name", "ProviderName", "NullProviderKey"];

    public static readonly string[] DefinitionsByName = ["Name"];

    public static readonly string[][] ValuesIndexes = [ValuesByNameProviderKey, ValuesByNameNullProviderKey];

    public static readonly string[][] DefinitionsIndexes = [DefinitionsByName];
}
