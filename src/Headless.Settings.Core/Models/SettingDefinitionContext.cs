// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
    public SettingDefinition Add(SettingDefinitionCreateOptions options)
    {
        Argument.IsNotNull(options);

        var definition = new SettingDefinition(
            options.Name,
            options.DefaultValue,
            options.DisplayName,
            options.Description,
            options.IsVisibleToClients,
            options.IsInherited,
            options.IsEncrypted
        );

        settings[definition.Name] = definition;

        return definition;
    }
}
