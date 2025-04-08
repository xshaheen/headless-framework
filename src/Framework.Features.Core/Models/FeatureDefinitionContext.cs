// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.Features.Models;

public sealed class FeatureDefinitionContext : IFeatureDefinitionContext
{
    internal Dictionary<string, FeatureGroupDefinition> Groups { get; } = new(StringComparer.Ordinal);

    public FeatureGroupDefinition AddGroup(string name, string? displayName = null)
    {
        Argument.IsNotNull(name);

        return AddGroup(new FeatureGroupDefinition(name, displayName));
    }

    public FeatureGroupDefinition AddGroup(FeatureGroupDefinition definition)
    {
        Argument.IsNotNull(definition);

        if (Groups.ContainsKey(definition.Name))
        {
            throw new InvalidOperationException(
                $"There is already an existing feature group with name: {definition.Name}"
            );
        }

        return Groups[definition.Name] = definition;
    }

    public FeatureGroupDefinition? GetGroupOrDefault(string name)
    {
        Argument.IsNotNull(name);

        return !Groups.TryGetValue(name, out var value) ? null : value;
    }

    public void RemoveGroup(string name)
    {
        Argument.IsNotNull(name);

        if (!Groups.ContainsKey(name))
        {
            throw new InvalidOperationException($"Undefined feature group: '{name}'.");
        }

        Groups.Remove(name);
    }
}
