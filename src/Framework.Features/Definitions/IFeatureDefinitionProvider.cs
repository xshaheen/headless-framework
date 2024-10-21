// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.Models;

namespace Framework.Features.Definitions;

public interface IFeatureDefinitionProvider
{
    void Define(IFeatureDefinitionContext context);
}
