// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.Features.Entities;
using Headless.Features.Models;
using Headless.Features.Repositories;
using Headless.Serializer.Modifiers;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace Headless.Features.Definitions;

/// <summary>Store for feature definitions that are defined dynamically from an external source such as a database.</summary>
public interface IDynamicFeatureDefinitionStore
{
    /// <summary>Returns the feature definition with the given <paramref name="name"/>, or <see langword="null"/> if not found.</summary>
    /// <param name="name">The unique feature name to look up.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="FeatureDefinition"/>, or <see langword="null"/> when absent.</returns>
    Task<FeatureDefinition?> GetOrDefaultAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns all feature definitions held in this dynamic store.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of all <see cref="FeatureDefinition"/> instances.</returns>
    Task<IReadOnlyList<FeatureDefinition>> GetFeaturesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all feature group definitions held in this dynamic store.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A read-only list of all <see cref="FeatureGroupDefinition"/> instances.</returns>
    Task<IReadOnlyList<FeatureGroupDefinition>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the application's static feature definitions into the dynamic store, creating, updating, or removing records as needed.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the distributed lock required for the save operation cannot be acquired.</exception>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
