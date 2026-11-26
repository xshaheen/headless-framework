// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;

namespace Headless.Settings;

/// <summary>Options that register the definition providers, value providers, and deleted settings for the settings management system.</summary>
public sealed class SettingManagementProvidersOptions
{
    /// <summary>
    /// Gets the ordered list of <see cref="ISettingDefinitionProvider"/> types that supply static setting definitions.
    /// Providers are invoked in list order; a later provider may overwrite a definition with the same name.
    /// </summary>
    public TypeList<ISettingDefinitionProvider> DefinitionProviders { get; } = [];

    /// <summary>
    /// Gets the ordered list of <see cref="ISettingValueReadProvider"/> types used to resolve setting values,
    /// lowest priority first — the last entry wins. <see cref="SettingValueProviderManager"/> reverses this
    /// list so readers consult providers highest priority first.
    /// </summary>
    /// <remarks>
    /// The list starts with the built-in chain (DefaultValue, Configuration, Global, Tenant, User), so a
    /// <c>Configure</c> callback sees it whether it was registered before or after <c>AddHeadlessSettings</c>.
    /// </remarks>
    public TypeList<ISettingValueReadProvider> ValueProviders { get; } = _CreateBuiltInChain();

    /// <summary>
    /// Gets the set of setting names that have been removed from the application and should be deleted
    /// from the dynamic store during the next save pass.
    /// </summary>
    public HashSet<string> DeletedSettings { get; } = [];

    private static TypeList<ISettingValueReadProvider> _CreateBuiltInChain()
    {
        var providers = new TypeList<ISettingValueReadProvider>();
        providers.Add<DefaultValueSettingValueProvider>();
        providers.Add<ConfigurationSettingValueProvider>();
        providers.Add<GlobalSettingValueProvider>();
        providers.Add<TenantSettingValueProvider>();
        providers.Add<UserSettingValueProvider>();

        return providers;
    }
}
