// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

/// <summary>
/// Retrieves setting definitions from the static store and falls back
/// to the dynamic store if not found in the static store.
/// </summary>
public interface ISettingDefinitionManager
{
    /// <summary>Returns all known setting definitions.</summary>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>A read-only list of all registered <see cref="SettingDefinition"/> instances.</returns>
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up a setting definition by name, returning <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique name of the setting.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>The matching <see cref="SettingDefinition"/>, or <see langword="null"/> if not registered.</returns>
    Task<SettingDefinition?> FindAsync(string name, CancellationToken cancellationToken = default);
}
