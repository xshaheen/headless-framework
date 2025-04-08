// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Models;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionProvider
{
    void Define(IFeatureDefinitionContext context);
}
