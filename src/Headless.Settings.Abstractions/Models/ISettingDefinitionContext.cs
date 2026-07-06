// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Models;

/// <summary>Provides access to the setting definition registry during the definition phase.</summary>
public interface ISettingDefinitionContext
{
    /// <summary>Returns the setting definition with the given name, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique name of the setting.</param>
    /// <returns>The registered <see cref="SettingDefinition"/>, or <see langword="null"/> if not present.</returns>
    SettingDefinition? GetOrDefault(string name);

    /// <summary>Returns all setting definitions registered so far.</summary>
    /// <returns>A read-only list of all <see cref="SettingDefinition"/> instances registered in this context.</returns>
    IReadOnlyList<SettingDefinition> GetAll();

    /// <summary>Creates, registers, and returns a setting definition with the given metadata.</summary>
    /// <param name="name">Unique name that identifies this setting.</param>
    /// <param name="defaultValue">Optional default value used when no provider supplies a value.</param>
    /// <param name="displayName">Human-readable name for UI surfaces; defaults to <paramref name="name"/> when not specified.</param>
    /// <param name="description">Optional descriptive text for the setting.</param>
    /// <param name="isVisibleToClients">
    /// Whether clients may read this setting's value. Defaults to <see langword="false"/> to prevent
    /// accidental exposure of sensitive values such as passwords.
    /// </param>
    /// <param name="isInherited">
    /// Whether the setting falls back to the next provider in the chain when no value is set.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="isEncrypted">
    /// Whether the value is stored encrypted in the data source. Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>The newly created and registered <see cref="SettingDefinition"/>, for further mutation (e.g. <see cref="SettingDefinition.Providers"/>).</returns>
    SettingDefinition Add(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = false,
        bool isInherited = true,
        bool isEncrypted = false
    );
}
