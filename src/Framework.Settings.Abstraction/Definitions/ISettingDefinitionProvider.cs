// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

/// <summary>Used to define a setting definition.</summary>
public interface ISettingDefinitionProvider
{
    void Define(ISettingDefinitionContext context);
}
