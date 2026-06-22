// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Settings.Definitions;
using Headless.Settings.ValueProviders;

namespace Headless.Settings.Models;

/// <summary>Options that register the definition providers, value providers, and deleted settings for the settings management system.</summary>
public sealed class SettingManagementProvidersOptions
{
    /// <summary>
    /// Gets the ordered list of <see cref="ISettingDefinitionProvider"/> types that supply static setting definitions.
    /// Providers are invoked in list order; a later provider may overwrite a definition with the same name.
    /// </summary>
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    /// <summary>
    /// Gets the ordered list of <see cref="ISettingValueReadProvider"/> types used to resolve setting values.
    /// Providers are consulted in list order until one returns a value.
    /// </summary>
    public TypeList<ISettingValueReadProvider> ValueProviders { get; } = [];

    /// <summary>
    /// Gets the set of setting names that have been removed from the application and should be deleted
    /// from the dynamic store during the next save pass.
    /// </summary>
    public HashSet<string> DeletedSettings { get; } = [];
}
