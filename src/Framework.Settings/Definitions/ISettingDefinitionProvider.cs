// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Settings.Definitions;

/// <summary>Used to define a setting definition.</summary>
public interface ISettingDefinitionProvider
{
    void Define(ISettingDefinitionContext context);
}

/// <inheritdoc />
public abstract class SettingDefinitionProvider : ISettingDefinitionProvider
{
    public abstract void Define(ISettingDefinitionContext context);
}
