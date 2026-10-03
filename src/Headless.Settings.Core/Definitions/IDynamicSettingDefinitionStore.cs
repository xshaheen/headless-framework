// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Serializer.Modifiers;
using Headless.Settings.Entities;
using Headless.Settings.Models;
using Headless.Settings.Repositories;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Headless.Settings.Definitions;

/// <summary>
/// Store for setting definitions that are defined dynamically from an external source such as a database.
/// </summary>
public interface IDynamicSettingDefinitionStore
{
    /// <summary>Returns all setting definitions held in the dynamic store.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A read-only list of all known <see cref="SettingDefinition"/> instances.</returns>
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the setting definition with the given <paramref name="name"/>, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique name of the setting definition to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching <see cref="SettingDefinition"/>, or <see langword="null"/>.</returns>
    Task<SettingDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists the current application's static settings to the dynamic store.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the distributed lock for the common stamp check or the cross-application save lock cannot be acquired.
    /// </exception>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
