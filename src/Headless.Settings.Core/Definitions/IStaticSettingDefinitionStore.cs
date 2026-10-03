// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Settings.Definitions;

/// <summary>
/// Store for setting definitions that are defined statically in the current application memory
/// via <see cref="SettingManagementProvidersOptions.DefinitionProviders"/>.
/// </summary>
public interface IStaticSettingDefinitionStore
{
    /// <summary>Returns all statically registered setting definitions.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A read-only list of all known <see cref="SettingDefinition"/> instances.</returns>
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the setting definition with the given <paramref name="name"/>, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique name of the setting definition to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching <see cref="SettingDefinition"/>, or <see langword="null"/>.</returns>
    Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);
}
