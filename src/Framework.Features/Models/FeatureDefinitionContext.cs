// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;

namespace Framework.Features.Models;

public interface IFeatureDefinitionContext
{
    FeatureGroupDefinition? GetGroupOrDefault(string name);

    FeatureGroupDefinition AddGroup(string name, string? displayName = null);

    void RemoveGroup(string name);
}

public sealed class FeatureDefinitionContext : IFeatureDefinitionContext
{
    internal Dictionary<string, FeatureGroupDefinition> Groups { get; } = new(StringComparer.Ordinal);

    public FeatureGroupDefinition AddGroup(string name, string? displayName = null)
    {
        Argument.IsNotNull(name);

        if (Groups.ContainsKey(name))
        {
            throw new InvalidOperationException($"There is already an existing feature group with name: {name}");
        }

        return Groups[name] = new FeatureGroupDefinition(name, displayName);
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
