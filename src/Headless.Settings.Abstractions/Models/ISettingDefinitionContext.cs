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

    /// <summary>Creates, registers, and returns a setting definition with the specified metadata.</summary>
    /// <param name="options">The setting name and optional metadata.</param>
    /// <returns>The newly created and registered <see cref="SettingDefinition"/>, for further mutation (e.g. <see cref="SettingDefinition.Providers"/>).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    SettingDefinition Add(SettingDefinitionCreateOptions options);
}
