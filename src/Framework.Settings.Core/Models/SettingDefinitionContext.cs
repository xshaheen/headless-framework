// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Settings.Models;

public sealed class SettingDefinitionContext(Dictionary<string, SettingDefinition> settings) : ISettingDefinitionContext
{
    public SettingDefinition? GetOrDefault(string name)
    {
        return settings.GetOrDefault(name);
    }

    public IReadOnlyList<SettingDefinition> GetAll()
    {
        return settings.Values.ToImmutableList();
    }

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
