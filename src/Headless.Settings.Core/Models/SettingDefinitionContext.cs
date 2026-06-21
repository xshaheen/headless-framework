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
    public void Add(params ReadOnlySpan<SettingDefinition> definitions)
    {
        if (definitions.IsEmpty)
        {
            return;
        }

        foreach (var definition in definitions)
        {
            settings[definition.Name] = definition;
        }
    }
}
