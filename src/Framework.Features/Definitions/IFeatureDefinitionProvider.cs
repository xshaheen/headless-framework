// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionProvider
{
    void Define(IFeatureDefinitionContext context);
}

public abstract class FeatureDefinitionProvider : IFeatureDefinitionProvider
{
    public abstract void Define(IFeatureDefinitionContext context);
}
