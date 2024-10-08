// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;

namespace Framework.Settings;

/// <summary>Used to define a setting definition.</summary>
public interface ISettingDefinitionProvider
{
    void Define(ISettingDefinitionContext context);
}
