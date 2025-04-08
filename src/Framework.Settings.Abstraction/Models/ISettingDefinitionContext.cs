// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Settings.Models;

public interface ISettingDefinitionContext
{
    SettingDefinition? GetOrDefault(string name);

    IReadOnlyList<SettingDefinition> GetAll();

    void Add(params ReadOnlySpan<SettingDefinition> definitions);
}
