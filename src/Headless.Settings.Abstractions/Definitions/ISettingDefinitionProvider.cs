// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;

namespace Headless.Settings.Definitions;

/// <summary>Used to define a setting definition.</summary>
public interface ISettingDefinitionProvider
{
    /// <summary>Registers setting definitions into the supplied <paramref name="context"/>.</summary>
    /// <param name="context">The context used to add or inspect setting definitions.</param>
    void Define(ISettingDefinitionContext context);
}
