// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings.ValueProviders;

/// <summary>Manages the ordered list of registered <see cref="ISettingValueReadProvider"/> instances.</summary>
public interface ISettingValueProviderManager
{
    /// <summary>
    /// Gets the registered setting value providers ordered by descending priority (user, tenant, global,
    /// configuration, default). Readers walk the list forward and take the first non-null value, so the
    /// entries after a requested provider are its fallback chain.
    /// </summary>
    IReadOnlyList<ISettingValueReadProvider> Providers { get; }
}
