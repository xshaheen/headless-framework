// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Models;

public interface IFeatureDefinitionContext
{
    FeatureGroupDefinition? GetGroupOrDefault(string name);

    FeatureGroupDefinition AddGroup(FeatureGroupDefinition definition);

    FeatureGroupDefinition AddGroup(string name, string? displayName = null);

    void RemoveGroup(string name);
}
