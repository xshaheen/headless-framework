// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Models;

/// <summary>Default implementation of <see cref="ISettingDefinitionContext"/> backed by an in-memory dictionary.</summary>
public sealed class SettingDefinitionContext(Dictionary<string, SettingDefinition> settings) : ISettingDefinitionContext
{
    /// <inheritdoc/>
    public SettingDefinition? GetOrDefault(string name)
    {
        return settings.GetOrDefault(name);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SettingDefinition> GetAll()
    {
        return settings.Values.ToImmutableList();
    }

    /// <inheritdoc/>
    public SettingDefinition Add(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = false,
        bool isInherited = true,
        bool isEncrypted = false
    )
    {
        var definition = new SettingDefinition(
            name,
            defaultValue,
            displayName,
            description,
            isVisibleToClients,
            isInherited,
            isEncrypted
        );

        settings[definition.Name] = definition;

        return definition;
    }
}
