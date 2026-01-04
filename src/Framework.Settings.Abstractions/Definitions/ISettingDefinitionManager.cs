// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

/// <summary>
/// Retrieves setting definitions from the static store and falls back
/// to the dynamic store if not found in the static store.
/// </summary>
public interface ISettingDefinitionManager
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SettingDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);
}
