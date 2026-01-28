// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

/// <summary>Used to define a setting definition.</summary>
public interface ISettingDefinitionProvider
{
    void Define(ISettingDefinitionContext context);
}
