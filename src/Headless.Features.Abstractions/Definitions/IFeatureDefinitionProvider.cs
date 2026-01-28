// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Models;

namespace Headless.Features.Definitions;

public interface IFeatureDefinitionProvider
{
    void Define(IFeatureDefinitionContext context);
}
